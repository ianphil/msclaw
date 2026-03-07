### Plan for Moving to Razor Pages for Azure AD Authentication

**Objective:**
To improve security and ease of implementing Azure AD authentication, consider transitioning from the current static `index.html` to ASP.NET Razor Pages.

**Benefits:**
1. **Stronger Integration with Authentication Middleware**: Razor Pages allow leveraging ASP.NET Core authentication and authorization middleware, offering tighter integration with Azure AD (e.g., token validation, claims management).
2. **Improved Security**: Razor Pages keep token management and sensitive logic server-side, reducing exposure of these details to the client.
3. **Simplified Azure AD Integration**: Razor Pages natively support Microsoft.Identity.Web, simplifying Azure AD configuration and eliminating the need for manual handling of redirect URIs.
4. **Dynamic and Scalable**: Razor Pages help dynamically serve user-specific content and manage roles (e.g., operators vs nodes).
5. **Future Expansion**: Easier support for role-based authentication and secure API integration.

**Migration Plan:**
1. **Set Up ASP.NET Razor Pages**:
   - Add Microsoft.Identity.Web for Azure AD authentication.
   - Configure OpenID Connect settings in `appsettings.json`.
   - Add Azure AD middleware in `Program.cs`.
2. **Replace the Current Static File**:
   - Move the logic in `index.html` to `Pages/Index.cshtml`.
   - Use Razor syntax to dynamically handle user data and Azure AD claims.
3. **Integrate SignalR**:
   - Set up SignalR for communication on the new Razor Pages architecture.
   - Modify the backend hub to work with authenticated users.
4. **Test Authentication Flow**:
   - Test end-to-end Azure AD authentication in development and staging environments.
   - Ensure secure token transmission and user authorization.

**Additional Considerations:**
- **Content Security Policy**: Apply CSP headers to prevent XSS attacks.
- **Role-Based Access Control**: Map user roles from Azure AD to backend logic for enhanced authorization.
- **Migration Logistics**: Plan a phased rollout to avoid disrupting users.

This issue item will serve as documentation for this planned improvement.