# FileMatrix Page Descriptions

## 1. Login/Authentication Page (`Views/Shared/_AuthModal.cshtml`)

### Function
A modal-based authentication system that provides both login and registration functionality in a single, elegant interface.

### Key Features
- **Dual Mode Operation**: Seamlessly switches between "Sign In" and "Create Account" modes
- **Modal Design**: Centered modal overlay with dark header and white content area
- **Dynamic Header**: Title and subtitle update based on active tab (Sign In / Create Account)
- **Form Toggle**: Footer link allows users to switch between login and registration without closing the modal
- **Responsive**: Adapts to mobile screens with adjusted padding and spacing
- **Visual Design**: 
  - Dark header (#020617) with gradient logo
  - Rounded form inputs with focus states
  - Gradient primary button with shadow effects
  - Smooth transitions between tabs

### User Flow
1. User clicks "Sign In" or "Get Started" button
2. Modal opens with login form by default
3. User can toggle to registration via footer link
4. Form submission handles authentication/registration
5. Modal closes after successful authentication

### Technical Details
- Uses Bootstrap modal component
- JavaScript handles tab switching and header updates
- Supports both login and registration partial views
- Client-side tab management for smooth UX

---

## 2. Admin Dashboard (`Areas/Admin/Views/Admin/Index.cshtml`)

### Function
Organization-level administrative dashboard providing an overview of documents, users, categories, and activity within a single organization/workspace.

### Key Features
- **Statistics Overview**: Four key metrics displayed as minimal stat cards
  - Documents: Total count of stored files
  - Active Users: Users signed in this week
  - Categories: Document organization categories
  - Recent Activity: Events in last 24 hours
  
- **Recent Activity Feed**: Timeline of recent actions within the organization
  - Organization setup events
  - User login activity
  - Document uploads
  - Links to full audit log

- **Recent Documents List**: Quick access to recently uploaded/modified documents
  - Document name and metadata
  - File size and owner information
  - Link to full documents page

- **Quick Actions Panel**: Common administrative tasks
  - Upload a document (primary action)
  - Manage users
  - Send invitations
  - Tidy up categories

### Design Characteristics
- **Minimal Aesthetic**: Clean white cards with subtle borders
- **Color Indicators**: Small colored dots for visual categorization
- **Hover Effects**: Subtle border color changes on interactive elements
- **Responsive Grid**: Adapts to different screen sizes
- **Typography**: Clear hierarchy with proper spacing

### Purpose
Designed for organization administrators to quickly understand their workspace status and access common management functions without navigating to separate pages.

---

## 3. Super Admin Dashboard (`Areas/SuperAdmin/Views/SuperAdmin/Index.cshtml`)

### Function
Platform-wide system administration dashboard for managing the entire FileMatrix platform across all organizations, users, and system resources.

### Key Features

#### System-Wide Statistics (4 Cards)
- **Organizations**: Total count with breakdown of active vs suspended
- **Total Users**: All users across all organizations with admin count
- **Documents**: System-wide document storage count
- **Active Sessions**: Currently online users across the platform

#### System Health Monitoring
- **Health Status Badge**: Real-time system health indicator (Healthy/Warning/Error)
- **Last Check Timestamp**: Shows when system was last verified
- **Operational Status**: Quick overview of system state

#### Storage Management
- **Storage Usage Bar**: Visual progress indicator with animated shimmer effect
- **Capacity Display**: Shows used vs total storage capacity
- **Percentage Indicator**: Clear percentage of capacity used

#### System Alerts
- **Warning Alerts**: Pending invitations requiring review
- **Info Alerts**: Recent signups across all organizations
- **Success Alerts**: System operational status confirmation

#### Recent System Activity
- Cross-organization activity timeline
- Organization creation events
- Bulk operations (user imports, backups)
- Security events and resolutions
- Organization suspension actions

#### Top Organizations
- List of largest organizations by user/document count
- Organization status indicators (Active/Suspended)
- Creation dates and metadata
- Quick access to organization management

#### Quick Actions Panel
- Create new organization
- Manage all organizations
- View all users system-wide
- Access system logs
- Security settings
- Backup & restore operations
- System configuration

### Design Characteristics
- **Distinct Visual Identity**: Dark sidebar (#1e2433) with orange/gold accents (vs purple for regular admin)
- **Geometric Icons**: CSS-based shapes instead of emojis for professional appearance
- **System-Level Focus**: All metrics and actions are platform-wide, not organization-specific
- **Enhanced Interactivity**: Hover effects, animations, and smooth transitions
- **Comprehensive Overview**: Single-page view of entire platform health and activity

### Purpose
Designed for platform administrators (super admins) who need to:
- Monitor system-wide health and performance
- Manage multiple organizations
- Oversee user accounts across all workspaces
- Handle system-level operations (backups, security, configuration)
- Track platform growth and usage metrics

### Access Control
- Separate area (`/SuperAdmin`) from regular admin (`/Admin`)
- Distinct layout and navigation
- System-wide permissions required
- Different visual theme to distinguish from organization-level admin

---

## Summary Comparison

| Feature | Login Page | Admin Dashboard | Super Admin Dashboard |
|---------|-----------|-----------------|----------------------|
| **Scope** | Authentication | Single Organization | Entire Platform |
| **Users** | All Users | Organization Admins | Platform Administrators |
| **Purpose** | User Access | Workspace Management | System Management |
| **Visual Theme** | Dark Header Modal | Light Purple Accents | Dark Sidebar, Orange Accents |
| **Key Metrics** | N/A | Org-level stats | Platform-wide stats |
| **Navigation** | Modal Overlay | Admin Sidebar | Super Admin Sidebar |
