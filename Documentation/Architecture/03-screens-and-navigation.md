# 4. Screen Inventory & Navigation Flow

One file per screen under `src/screens/<Module>/`.

| Module | Screens |
|---|---|
| Splash | SplashScreen |
| Authentication | LoginScreen, AdminForgotPasswordScreen, AdminResetPasswordScreen |
| Dashboard | ConsolidatedDashboardScreen (Admin), BusinessDashboardScreen |
| Business | BusinessListScreen, BusinessDetailScreen, BusinessFormScreen, BusinessMembersScreen, MemberPermissionsScreen |
| Members | MemberListScreen, MemberFormScreen, MemberDetailScreen, SetMemberPasswordScreen (Admin) |
| Customers | CustomerListScreen, CustomerDetailScreen, CustomerFormScreen, CustomerLookupScreen |
| Products | ProductListScreen, ProductFormScreen, ProductBusinessMappingScreen |
| Expenses | ExpenseListScreen, ExpenseFormScreen, ExpenseSummaryScreen (total + member-wise, basis toggle) |
| Sales | SalesListScreen, SalesOrderFormScreen (customer lookup + item picker), SalesOrderDetailScreen |
| Payments | PaymentHistoryScreen, RecordPaymentScreen |
| Invoices | InvoiceDetailScreen, InvoicePdfViewerScreen |
| Reports | ReportsHomeScreen, ReportDetailScreen |
| Admin | AuditLogScreen, FinancialAdjustmentsScreen |
| Profile / Settings | ProfileScreen, EditProfileScreen, ChangePasswordScreen, SettingsScreen |

## Flow
```
Splash → (valid refresh token?) ─yes→ MainNavigator
                              └no──→ AuthNavigator: Login ⇄ AdminForgot → AdminReset
MainNavigator (bottom tabs; tabs filtered by role/permission)
 ├ Dashboard  (Admin: consolidated · Member: business picker → BusinessDashboard)
 ├ Businesses → Detail → [Customers | Products | Expenses | Sales → Detail → Payments/Invoice | Reports]
 ├ Admin tab (Admin only): Members, Products, Audit, Adjustments
 └ Profile → Settings / Change password
```
Route guard: `ProtectedRoute` wrapper checks role/permission from store before rendering; server still returns 403/404 on direct calls.
