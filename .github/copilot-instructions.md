# Copilot Instructions

## General Guidelines
- Save and reuse the exact modular ASP.NET project structure in the workspace (FileMatrix(Pabiran)).
- Use this structure for consistency across the project.

## Project Structure
- Enforce the following project structure:
  - **Areas**
    - Admin (Controllers, Views, Models)
  - **Controllers**
    - Home
    - Account
    - Organizations
    - Documents
    - Folders
    - Notifications
    - Audit
  - **Models**
    - ViewModels & DTOs
  - **Views**
    - Shared and per-feature folders
  - **wwwroot**
    - css
    - js
    - lib
    - images
    - uploads/.gitkeep
  - **Services**
    - EmailSenderService
    - GoogleDriveService
    - OrganizationService
    - DocumentService
    - PermissionService
  - **Data**
    - ApplicationDbContext
    - Migrations
  - **ViewComponents**
    - DocumentListViewComponent
    - FolderTreeViewComponent
    - OrgSwitcherViewComponent
    - NotificationBadgeViewComponent
    - RecentDocumentsViewComponent
  - **Global**
    - _ViewImports.cshtml