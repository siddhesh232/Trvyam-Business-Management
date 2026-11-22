using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Infrastructure.Persistence;
using BusinessManagement.Infrastructure.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BusinessManagement.Tests.Database;

public sealed class DatabaseTests(DbFixture fx) : IClassFixture<DbFixture>
{
    private static Customer NewCustomer(string? mobile = null) =>
        new() { Mobile = mobile ?? DbFixture.NewMobile(), Name = "Test Customer" };

    private async Task<SalesOrder> NewOrderAsync(AppDbContext db, decimal final = 1180m)
    {
        var customer = NewCustomer();
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var order = new SalesOrder
        {
            BusinessId = fx.BusinessId, CustomerId = customer.Id, OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Subtotal = 1000m, DiscountAmount = 0, TaxableAmount = 1000m, GstAmount = final - 1000m, FinalAmount = final,
            CreatedBy = fx.AdminId
        };
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private static Payment NewPayment(long orderId, decimal amount, long receivedBy) => new()
    {
        SalesOrderId = orderId, PaymentDate = DateOnly.FromDateTime(DateTime.UtcNow), Amount = amount,
        PaymentMode = PaymentMode.Upi, ReceivedBy = receivedBy
    };

    // ---------------------------------------------------------------- schema constraints
    [SqlFact]
    public async Task Only_one_admin_account_can_exist()
    {
        await using var db = fx.Create();
        db.Users.Add(new User { Role = UserRole.Admin, Name = "Second", Username = "admin2", PasswordHash = "x", Mobile = "9111111111", Email = "a2@example.test" });
        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [SqlFact]
    public async Task Members_may_omit_email_address()
    {
        await using var db = fx.Create();
        db.Users.Add(new User { Role = UserRole.Member, Name = "No Email", Username = "noemail" + Guid.NewGuid().ToString("N")[..6], PasswordHash = "x", Mobile = DbFixture.NewMobile() });
        await db.SaveChangesAsync(); // members may have no email
    }

    [SqlFact]
    public async Task Customer_mobile_is_unique_across_businesses_but_reusable_after_soft_delete()
    {
        var mobile = DbFixture.NewMobile();
        await using var db = fx.CreateAsAdmin();
        var first = NewCustomer(mobile);
        db.Customers.Add(first);
        await db.SaveChangesAsync();

        await using (var dup = fx.CreateAsAdmin())
        {
            dup.Customers.Add(NewCustomer(mobile));
            var act = () => dup.SaveChangesAsync();
            await act.Should().ThrowAsync<DbUpdateException>();
        }

        db.Customers.Remove(first);
        await db.SaveChangesAsync();                       // soft delete
        await using var again = fx.CreateAsAdmin();
        again.Customers.Add(NewCustomer(mobile));
        await again.SaveChangesAsync();                    // allowed now
    }

    [SqlFact]
    public async Task Customer_mobile_must_be_numeric()
    {
        await using var db = fx.CreateAsAdmin();
        db.Customers.Add(NewCustomer("98ab567890"));
        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [SqlFact]
    public async Task Expense_total_is_computed_by_the_database()
    {
        await using var db = fx.CreateAsAdmin();
        var e = new Expense
        {
            BusinessId = fx.BusinessId, ExpenseDate = DateOnly.FromDateTime(DateTime.UtcNow), Name = "Nails",
            Quantity = 3, UnitPrice = 12.50m, PurchasedByUserId = fx.MemberId
        };
        db.Expenses.Add(e);
        await db.SaveChangesAsync();
        e.TotalAmount.Should().Be(37.50m);
        e.CreatedBy.Should().Be(fx.AdminId, "CreatedBy is stamped from the current user");
    }

    [SqlFact]
    public async Task Permissions_require_an_existing_business_membership()
    {
        await using var db = fx.CreateAsAdmin();
        db.BusinessPermissions.Add(new BusinessPermission { BusinessId = fx.BusinessId, UserId = fx.AdminId });
        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>("admin is not a BusinessMember, so no permission row may exist");
    }

    [SqlFact]
    public async Task Payment_amount_must_be_positive_and_reversal_needs_a_reason()
    {
        await using var db = fx.CreateAsAdmin();
        var order = await NewOrderAsync(db);

        db.Payments.Add(NewPayment(order.Id, 0m, fx.AdminId));
        var zero = () => db.SaveChangesAsync();
        await zero.Should().ThrowAsync<DbUpdateException>();

        await using var db2 = fx.CreateAsAdmin();
        var bad = NewPayment(order.Id, 100m, fx.AdminId);
        bad.Status = PaymentRecordStatus.Reversed; bad.ReversedAt = DateTime.UtcNow;   // no reason
        db2.Payments.Add(bad);
        var noReason = () => db2.SaveChangesAsync();
        await noReason.Should().ThrowAsync<DbUpdateException>();
    }

    // ---------------------------------------------------------------- payment status (calculated)
    [SqlFact]
    public async Task Payment_status_is_calculated_and_ignores_reversed_payments()
    {
        await using var db = fx.CreateAsAdmin();
        var order = await NewOrderAsync(db, 1180m);

        async Task<SalesOrderPaymentSummary> Summary() =>
            await fx.Create().SalesOrderPaymentSummaries.AsNoTracking().SingleAsync(s => s.SalesOrderId == order.Id);

        (await Summary()).PaymentStatus.Should().Be("Unpaid");

        var p1 = NewPayment(order.Id, 400m, fx.MemberId);
        db.Payments.Add(p1);
        await db.SaveChangesAsync();
        var s1 = await Summary();
        (s1.PaymentStatus, s1.TotalReceived, s1.Outstanding).Should().Be(("PartiallyPaid", 400m, 780m));

        var reversed = NewPayment(order.Id, 300m, fx.MemberId);
        reversed.Status = PaymentRecordStatus.Reversed; reversed.ReversedAt = DateTime.UtcNow;
        reversed.ReversedBy = fx.AdminId; reversed.ReversalReason = "Cheque bounced";
        db.Payments.Add(reversed);
        await db.SaveChangesAsync();
        (await Summary()).TotalReceived.Should().Be(400m, "reversed payments are kept for audit but not counted");

        db.Payments.Add(NewPayment(order.Id, 780m, fx.MemberId));
        await db.SaveChangesAsync();
        var s3 = await Summary();
        (s3.PaymentStatus, s3.Outstanding).Should().Be(("Paid", 0m));
    }

    // ---------------------------------------------------------------- audit + soft delete
    [SqlFact]
    public async Task Creating_a_record_writes_an_audit_row_with_the_acting_user()
    {
        await using var db = fx.CreateAsAdmin();
        var c = NewCustomer();
        db.Customers.Add(c);
        await db.SaveChangesAsync();

        var audit = await fx.Create().AuditLogs.SingleAsync(a => a.EntityName == "Customer" && a.EntityId == c.Id);
        audit.Action.Should().Be(AuditAction.Create);
        audit.UserId.Should().Be(fx.AdminId);
        audit.NewValues.Should().Contain(c.Mobile);
        audit.IpAddress.Should().Be("127.0.0.1");
    }

    [SqlFact]
    public async Task Updating_a_record_audits_only_the_changed_fields()
    {
        await using var db = fx.CreateAsAdmin();
        var c = NewCustomer();
        db.Customers.Add(c);
        await db.SaveChangesAsync();

        c.Name = "Renamed Customer";
        await db.SaveChangesAsync();

        var audit = await fx.Create().AuditLogs.SingleAsync(a => a.EntityName == "Customer" && a.EntityId == c.Id && a.Action == AuditAction.Update);
        audit.OldValues.Should().Contain("Test Customer").And.NotContain(c.Mobile);
        audit.NewValues.Should().Contain("Renamed Customer");
    }

    [SqlFact]
    public async Task Sensitive_fields_never_reach_the_audit_log()
    {
        await using var db = fx.CreateAsAdmin();
        var hash = new Pbkdf2PasswordHasher().Hash("Sup3r-Secret-Pass");
        var u = new User { Role = UserRole.Member, Name = "Audit Me", Username = "audit" + Guid.NewGuid().ToString("N")[..6], PasswordHash = hash, Mobile = DbFixture.NewMobile() };
        db.Users.Add(u);
        await db.SaveChangesAsync();

        var audit = await fx.Create().AuditLogs.SingleAsync(a => a.EntityName == "User" && a.EntityId == u.Id);
        audit.NewValues.Should().NotContain("PasswordHash").And.NotContain(hash);
    }

    [SqlFact]
    public async Task Deleting_a_financial_record_is_a_soft_delete_with_audit()
    {
        await using var db = fx.CreateAsAdmin();
        var e = new Expense { BusinessId = fx.BusinessId, ExpenseDate = DateOnly.FromDateTime(DateTime.UtcNow), Name = "Tape", UnitPrice = 50m, PurchasedByUserId = fx.MemberId };
        db.Expenses.Add(e);
        await db.SaveChangesAsync();

        db.Expenses.Remove(e);
        await db.SaveChangesAsync();

        await using var check = fx.Create();
        (await check.Expenses.AnyAsync(x => x.Id == e.Id)).Should().BeFalse("query filter hides soft-deleted rows");
        var row = await check.Expenses.IgnoreQueryFilters().SingleAsync(x => x.Id == e.Id);
        row.IsDeleted.Should().BeTrue();
        row.DeletedBy.Should().Be(fx.AdminId);
        row.DeletedAt.Should().NotBeNull();
        (await check.AuditLogs.AnyAsync(a => a.EntityName == "Expense" && a.EntityId == e.Id && a.Action == AuditAction.Delete)).Should().BeTrue();
    }

    // ---------------------------------------------------------------- invoices
    private GstInvoice NewInvoice(long orderId, int seq, string number) => new()
    {
        BusinessId = fx.BusinessId, SalesOrderId = orderId, FinancialYear = "2026-27", SequenceNumber = seq,
        InvoiceNumber = number, InvoiceDate = DateOnly.FromDateTime(DateTime.UtcNow), IsInterState = false,
        TaxableAmount = 1000m, Cgst = 90m, Sgst = 90m, Igst = 0m, GrandTotal = 1180m, SnapshotJson = "{\"v\":1}",
        IssuedBy = fx.AdminId, IssuedAt = DateTime.UtcNow
    };

    [SqlFact]
    public async Task Invoice_numbers_are_unique_per_business_and_one_active_invoice_per_order()
    {
        await using var db = fx.CreateAsAdmin();
        var order = await NewOrderAsync(db);
        var tag = Guid.NewGuid().ToString("N")[..4];
        db.GstInvoices.Add(NewInvoice(order.Id, 9000 + Random.Shared.Next(1, 500), $"INV/{tag}/1"));
        await db.SaveChangesAsync();

        await using var dup = fx.CreateAsAdmin();
        dup.GstInvoices.Add(NewInvoice(order.Id, 9501 + Random.Shared.Next(1, 400), $"INV/{tag}/2"));
        var act = () => dup.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>("an order may have only one active tax invoice");
    }

    [SqlFact]
    public async Task Issued_invoice_content_is_immutable_but_it_can_be_cancelled()
    {
        await using var db = fx.CreateAsAdmin();
        var order = await NewOrderAsync(db);
        var inv = NewInvoice(order.Id, 9900 + Random.Shared.Next(1, 90), ("INV/" + Guid.NewGuid().ToString("N"))[..20]);
        db.GstInvoices.Add(inv);
        await db.SaveChangesAsync();

        inv.SnapshotJson = "{\"tampered\":true}";
        var tamper = () => db.SaveChangesAsync();
        await tamper.Should().ThrowAsync<DbUpdateException>("trigger blocks content changes");

        await using var db2 = fx.CreateAsAdmin();
        var again = await db2.GstInvoices.SingleAsync(i => i.Id == inv.Id);
        again.Status = InvoiceStatus.Cancelled; again.CancelReason = "Wrong customer";
        await db2.SaveChangesAsync();                       // status change is permitted

        await using var db3 = fx.CreateAsAdmin();
        db3.GstInvoices.Remove(await db3.GstInvoices.SingleAsync(i => i.Id == inv.Id));
        var del = () => db3.SaveChangesAsync();
        await del.Should().ThrowAsync<DbUpdateException>("invoices can never be deleted");
    }

    // ---------------------------------------------------------------- seeding
    [SqlFact]
    public async Task Seeder_does_nothing_when_an_admin_already_exists()
    {
        await using var db = fx.Create();
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seed:AdminUsername"] = "other", ["Seed:AdminPassword"] = "A-Very-Long-Password-1", ["Seed:AdminEmail"] = "o@example.test"
        }).Build();
        (await DbSeeder.SeedAdminAsync(db, new Pbkdf2PasswordHasher(), cfg)).Should().BeFalse();
        (await db.Users.CountAsync(u => u.Role == UserRole.Admin)).Should().Be(1);
    }
}
