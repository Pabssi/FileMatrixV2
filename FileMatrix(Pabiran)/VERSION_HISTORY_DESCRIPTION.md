# Version History Function Description

## Version History

**Location**: `Areas/Admin/Views/Documents/Index.cshtml` (Modal component)  
**Access**: Click "Version History" from document card dropdown menu

The Version History feature functionally serves as a document version control system that allows users to upload updated versions of existing documents, view a chronological timeline of all document versions with complete metadata (upload date, user who uploaded it, description of changes, and file size), download any previous version of the document, restore any previous version to make it the current active version, and track which version is currently active with a visual "Current" badge. When a user uploads a new version through the "Upload New Version" area at the top of the modal, the system creates a new version entry, preserves all previous versions for historical reference, automatically marks the new upload as the current version, and allows users to revert to any older version using the "Restore" button on any previous version card. This is essentially a document update/edit page with full version tracking and rollback capabilities, ensuring that document changes are never lost and can be reverted if needed.
