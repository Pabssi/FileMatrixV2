IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [AspNetRoles] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(128) NULL,
    [NormalizedName] nvarchar(128) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetUsers] (
    [Id] int NOT NULL IDENTITY,
    [UserName] nvarchar(256) NULL,
    [NormalizedUserName] nvarchar(256) NULL,
    [Email] nvarchar(256) NULL,
    [NormalizedEmail] nvarchar(256) NULL,
    [EmailConfirmed] bit NOT NULL,
    [PasswordHash] nvarchar(max) NULL,
    [SecurityStamp] nvarchar(max) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    [PhoneNumber] nvarchar(max) NULL,
    [PhoneNumberConfirmed] bit NOT NULL,
    [TwoFactorEnabled] bit NOT NULL,
    [LockoutEnd] datetimeoffset NULL,
    [LockoutEnabled] bit NOT NULL,
    [AccessFailedCount] int NOT NULL,
    CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
);

CREATE TABLE [Users] (
    [UserID] int NOT NULL IDENTITY,
    [Username] nvarchar(100) NOT NULL,
    [Email] nvarchar(255) NOT NULL,
    [PasswordHash] nvarchar(max) NOT NULL,
    [FirstName] nvarchar(100) NULL,
    [LastName] nvarchar(100) NULL,
    [DisplayName] nvarchar(150) NULL,
    [Role] int NOT NULL DEFAULT 3,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
    [LastLogin] datetime2 NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([UserID])
);

CREATE TABLE [AspNetRoleClaims] (
    [Id] int NOT NULL IDENTITY,
    [RoleId] int NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserClaims] (
    [Id] int NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserLogins] (
    [LoginProvider] nvarchar(128) NOT NULL,
    [ProviderKey] nvarchar(128) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId] int NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserRoles] (
    [UserId] int NOT NULL,
    [RoleId] int NOT NULL,
    CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
    CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserTokens] (
    [UserId] int NOT NULL,
    [LoginProvider] nvarchar(128) NOT NULL,
    [Name] nvarchar(128) NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [Workplaces] (
    [WorkplaceID] int NOT NULL IDENTITY,
    [Name] nvarchar(200) NOT NULL,
    [Slug] nvarchar(100) NULL,
    [CreatedByUserID] int NULL,
    [LogoURL] nvarchar(500) NULL,
    [Description] nvarchar(max) NULL,
    [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    CONSTRAINT [PK_Workplaces] PRIMARY KEY ([WorkplaceID]),
    CONSTRAINT [FK_Workplaces_Users_CreatedByUserID] FOREIGN KEY ([CreatedByUserID]) REFERENCES [Users] ([UserID]) ON DELETE NO ACTION
);

CREATE TABLE [AuditLogs] (
    [LogID] bigint NOT NULL IDENTITY,
    [WorkplaceID] int NOT NULL,
    [Action] nvarchar(50) NOT NULL,
    [EntityType] nvarchar(50) NOT NULL,
    [EntityID] int NOT NULL,
    [UserID] int NULL,
    [PerformedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
    [Details] nvarchar(max) NULL,
    [IpAddress] nvarchar(max) NULL,
    CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([LogID]),
    CONSTRAINT [FK_AuditLogs_Users_UserID] FOREIGN KEY ([UserID]) REFERENCES [Users] ([UserID]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AuditLogs_Workplaces_WorkplaceID] FOREIGN KEY ([WorkplaceID]) REFERENCES [Workplaces] ([WorkplaceID]) ON DELETE CASCADE
);

CREATE TABLE [Folders] (
    [FolderID] int NOT NULL IDENTITY,
    [WorkplaceID] int NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [ParentFolderID] int NULL,
    [CreatedByUserID] int NULL,
    [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
    CONSTRAINT [PK_Folders] PRIMARY KEY ([FolderID]),
    CONSTRAINT [FK_Folders_Folders_ParentFolderID] FOREIGN KEY ([ParentFolderID]) REFERENCES [Folders] ([FolderID]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Folders_Users_CreatedByUserID] FOREIGN KEY ([CreatedByUserID]) REFERENCES [Users] ([UserID]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Folders_Workplaces_WorkplaceID] FOREIGN KEY ([WorkplaceID]) REFERENCES [Workplaces] ([WorkplaceID]) ON DELETE NO ACTION
);

CREATE TABLE [WorkplaceInvitations] (
    [InvitationID] int NOT NULL IDENTITY,
    [WorkplaceID] int NOT NULL,
    [InvitedByUserID] int NULL,
    [Email] nvarchar(255) NOT NULL,
    [Token] nvarchar(100) NOT NULL,
    [Code] nvarchar(20) NULL,
    [InvitePassword] nvarchar(100) NULL,
    [ExpiresAt] datetime2 NULL,
    [Status] nvarchar(20) NULL DEFAULT N'pending',
    [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
    CONSTRAINT [PK_WorkplaceInvitations] PRIMARY KEY ([InvitationID]),
    CONSTRAINT [FK_WorkplaceInvitations_Users_InvitedByUserID] FOREIGN KEY ([InvitedByUserID]) REFERENCES [Users] ([UserID]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WorkplaceInvitations_Workplaces_WorkplaceID] FOREIGN KEY ([WorkplaceID]) REFERENCES [Workplaces] ([WorkplaceID]) ON DELETE CASCADE
);

CREATE TABLE [WorkplaceMembers] (
    [WorkplaceMemberID] int NOT NULL IDENTITY,
    [WorkplaceID] int NOT NULL,
    [UserID] int NOT NULL,
    [RoleID] int NOT NULL DEFAULT 3,
    [JoinedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
    CONSTRAINT [PK_WorkplaceMembers] PRIMARY KEY ([WorkplaceMemberID]),
    CONSTRAINT [FK_WorkplaceMembers_Users_UserID] FOREIGN KEY ([UserID]) REFERENCES [Users] ([UserID]) ON DELETE CASCADE,
    CONSTRAINT [FK_WorkplaceMembers_Workplaces_WorkplaceID] FOREIGN KEY ([WorkplaceID]) REFERENCES [Workplaces] ([WorkplaceID]) ON DELETE CASCADE
);

CREATE TABLE [DocumentPermissions] (
    [PermissionID] int NOT NULL IDENTITY,
    [DocumentID] int NOT NULL,
    [RoleName] nvarchar(max) NULL,
    [UserID] int NULL,
    [PermissionLevel] nvarchar(20) NOT NULL,
    CONSTRAINT [PK_DocumentPermissions] PRIMARY KEY ([PermissionID]),
    CONSTRAINT [FK_DocumentPermissions_Users_UserID] FOREIGN KEY ([UserID]) REFERENCES [Users] ([UserID]) ON DELETE NO ACTION
);

CREATE TABLE [Documents] (
    [DocumentID] int NOT NULL IDENTITY,
    [WorkplaceID] int NOT NULL,
    [Title] nvarchar(300) NOT NULL,
    [Description] nvarchar(max) NULL,
    [FolderID] int NULL,
    [CurrentVersionID] int NULL,
    [CreatedByUserID] int NULL,
    [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
    [UpdatedAt] datetime2 NULL,
    [GoogleDriveFileID] nvarchar(max) NULL,
    [GoogleDriveLink] nvarchar(max) NULL,
    [BusinessEntityType] nvarchar(max) NULL,
    [BusinessEntityID] int NULL,
    CONSTRAINT [PK_Documents] PRIMARY KEY ([DocumentID]),
    CONSTRAINT [FK_Documents_Folders_FolderID] FOREIGN KEY ([FolderID]) REFERENCES [Folders] ([FolderID]) ON DELETE SET NULL,
    CONSTRAINT [FK_Documents_Users_CreatedByUserID] FOREIGN KEY ([CreatedByUserID]) REFERENCES [Users] ([UserID]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Documents_Workplaces_WorkplaceID] FOREIGN KEY ([WorkplaceID]) REFERENCES [Workplaces] ([WorkplaceID]) ON DELETE NO ACTION
);

CREATE TABLE [DocumentVersions] (
    [VersionID] int NOT NULL IDENTITY,
    [DocumentID] int NOT NULL,
    [VersionNumber] decimal(10,2) NOT NULL,
    [FileName] nvarchar(300) NOT NULL,
    [FilePath] nvarchar(500) NOT NULL,
    [FileSizeBytes] bigint NOT NULL,
    [MimeType] nvarchar(100) NOT NULL,
    [UploadedByUserID] int NULL,
    [UploadedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
    [ChangeNote] nvarchar(500) NULL,
    [RestoredFromID] int NULL,
    CONSTRAINT [PK_DocumentVersions] PRIMARY KEY ([VersionID]),
    CONSTRAINT [FK_DocumentVersions_DocumentVersions_RestoredFromID] FOREIGN KEY ([RestoredFromID]) REFERENCES [DocumentVersions] ([VersionID]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DocumentVersions_Documents_DocumentID] FOREIGN KEY ([DocumentID]) REFERENCES [Documents] ([DocumentID]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DocumentVersions_Users_UploadedByUserID] FOREIGN KEY ([UploadedByUserID]) REFERENCES [Users] ([UserID]) ON DELETE NO ACTION
);

CREATE TABLE [Notifications] (
    [NotificationID] int NOT NULL IDENTITY,
    [WorkplaceID] int NOT NULL,
    [RecipientUserID] int NULL,
    [Type] nvarchar(50) NOT NULL,
    [Message] nvarchar(max) NOT NULL,
    [DocumentID] int NULL,
    [IsSent] bit NOT NULL DEFAULT CAST(0 AS bit),
    [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
    [SentAt] datetime2 NULL,
    CONSTRAINT [PK_Notifications] PRIMARY KEY ([NotificationID]),
    CONSTRAINT [FK_Notifications_Documents_DocumentID] FOREIGN KEY ([DocumentID]) REFERENCES [Documents] ([DocumentID]) ON DELETE SET NULL,
    CONSTRAINT [FK_Notifications_Users_RecipientUserID] FOREIGN KEY ([RecipientUserID]) REFERENCES [Users] ([UserID]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Notifications_Workplaces_WorkplaceID] FOREIGN KEY ([WorkplaceID]) REFERENCES [Workplaces] ([WorkplaceID]) ON DELETE CASCADE
);

CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);

CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;

CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);

CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);

CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);

CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);

CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;

CREATE INDEX [IX_AuditLogs_UserID] ON [AuditLogs] ([UserID]);

CREATE INDEX [IX_AuditLogs_WorkplaceID] ON [AuditLogs] ([WorkplaceID]);

CREATE INDEX [IX_DocumentPermissions_DocumentID] ON [DocumentPermissions] ([DocumentID]);

CREATE INDEX [IX_DocumentPermissions_UserID] ON [DocumentPermissions] ([UserID]);

CREATE INDEX [IX_Documents_CreatedByUserID] ON [Documents] ([CreatedByUserID]);

CREATE INDEX [IX_Documents_CurrentVersionID] ON [Documents] ([CurrentVersionID]);

CREATE INDEX [IX_Documents_FolderID] ON [Documents] ([FolderID]);

CREATE INDEX [IX_Documents_WorkplaceID] ON [Documents] ([WorkplaceID]);

CREATE INDEX [IX_DocumentVersions_DocumentID] ON [DocumentVersions] ([DocumentID]);

CREATE INDEX [IX_DocumentVersions_RestoredFromID] ON [DocumentVersions] ([RestoredFromID]);

CREATE INDEX [IX_DocumentVersions_UploadedByUserID] ON [DocumentVersions] ([UploadedByUserID]);

CREATE INDEX [IX_Folders_CreatedByUserID] ON [Folders] ([CreatedByUserID]);

CREATE INDEX [IX_Folders_ParentFolderID] ON [Folders] ([ParentFolderID]);

CREATE INDEX [IX_Folders_WorkplaceID] ON [Folders] ([WorkplaceID]);

CREATE INDEX [IX_Notifications_DocumentID] ON [Notifications] ([DocumentID]);

CREATE INDEX [IX_Notifications_RecipientUserID] ON [Notifications] ([RecipientUserID]);

CREATE INDEX [IX_Notifications_WorkplaceID] ON [Notifications] ([WorkplaceID]);

CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);

CREATE UNIQUE INDEX [IX_Users_Username] ON [Users] ([Username]);

CREATE INDEX [IX_WorkplaceInvitations_InvitedByUserID] ON [WorkplaceInvitations] ([InvitedByUserID]);

CREATE INDEX [IX_WorkplaceInvitations_WorkplaceID] ON [WorkplaceInvitations] ([WorkplaceID]);

CREATE INDEX [IX_WorkplaceMembers_UserID] ON [WorkplaceMembers] ([UserID]);

CREATE UNIQUE INDEX [IX_WorkplaceMembers_WorkplaceID_UserID] ON [WorkplaceMembers] ([WorkplaceID], [UserID]);

CREATE INDEX [IX_Workplaces_CreatedByUserID] ON [Workplaces] ([CreatedByUserID]);

CREATE UNIQUE INDEX [IX_Workplaces_Slug] ON [Workplaces] ([Slug]) WHERE [Slug] IS NOT NULL;

ALTER TABLE [DocumentPermissions] ADD CONSTRAINT [FK_DocumentPermissions_Documents_DocumentID] FOREIGN KEY ([DocumentID]) REFERENCES [Documents] ([DocumentID]) ON DELETE CASCADE;

ALTER TABLE [Documents] ADD CONSTRAINT [FK_Documents_DocumentVersions_CurrentVersionID] FOREIGN KEY ([CurrentVersionID]) REFERENCES [DocumentVersions] ([VersionID]) ON DELETE SET NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260214083054_rawr', N'10.0.3');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [Documents] ADD [CategoryID] int NULL;

CREATE TABLE [Categories] (
    [CategoryID] int NOT NULL IDENTITY,
    [WorkplaceID] int NOT NULL,
    [Name] nvarchar(100) NOT NULL,
    [Icon] nvarchar(max) NULL,
    [Color] nvarchar(max) NULL,
    [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
    CONSTRAINT [PK_Categories] PRIMARY KEY ([CategoryID]),
    CONSTRAINT [FK_Categories_Workplaces_WorkplaceID] FOREIGN KEY ([WorkplaceID]) REFERENCES [Workplaces] ([WorkplaceID]) ON DELETE CASCADE
);

CREATE INDEX [IX_Documents_CategoryID] ON [Documents] ([CategoryID]);

CREATE INDEX [IX_Categories_WorkplaceID] ON [Categories] ([WorkplaceID]);

ALTER TABLE [Documents] ADD CONSTRAINT [FK_Documents_Categories_CategoryID] FOREIGN KEY ([CategoryID]) REFERENCES [Categories] ([CategoryID]) ON DELETE SET NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260214190713_AddCategories', N'10.0.3');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [WorkplaceInvitations] ADD [RoleID] int NOT NULL DEFAULT 3;

ALTER TABLE [WorkplaceInvitations] ADD [UsedAt] datetime2 NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260215085329_AddInvitationDetails', N'10.0.3');

COMMIT;
GO

BEGIN TRANSACTION;

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Documents]') AND name = 'IsFavorite')
                BEGIN
                    ALTER TABLE [Documents] ADD [IsFavorite] bit NOT NULL DEFAULT 0;
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Documents]') AND name = 'PublicShareToken')
                BEGIN
                    ALTER TABLE [Documents] ADD [PublicShareToken] nvarchar(max) NULL;
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Documents]') AND name = 'Status')
                BEGIN
                    ALTER TABLE [Documents] ADD [Status] nvarchar(20) NOT NULL DEFAULT 'Published';
                END

                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[DocumentComments]') AND type in (N'U'))
                BEGIN
                    CREATE TABLE [DocumentComments] (
                        [CommentID] int NOT NULL IDENTITY,
                        [DocumentID] int NOT NULL,
                        [UserID] int NOT NULL,
                        [Text] nvarchar(max) NOT NULL,
                        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
                        CONSTRAINT [PK_DocumentComments] PRIMARY KEY ([CommentID]),
                        CONSTRAINT [FK_DocumentComments_Documents_DocumentID] FOREIGN KEY ([DocumentID]) REFERENCES [Documents] ([DocumentID]) ON DELETE CASCADE,
                        CONSTRAINT [FK_DocumentComments_Users_UserID] FOREIGN KEY ([UserID]) REFERENCES [Users] ([UserID]) ON DELETE NO ACTION
                    );

                    CREATE INDEX [IX_DocumentComments_DocumentID] ON [DocumentComments] ([DocumentID]);
                    CREATE INDEX [IX_DocumentComments_UserID] ON [DocumentComments] ([UserID]);
                END
            

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260216153426_AddPublicSharing', N'10.0.3');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [Documents] ADD [ArchivedAt] datetime2 NULL;

ALTER TABLE [Documents] ADD [RetentionNoticeSent] bit NOT NULL DEFAULT CAST(0 AS bit);

CREATE TABLE [RetentionPolicies] (
    [ID] int NOT NULL IDENTITY,
    [WorkplaceID] int NOT NULL,
    [AutoArchiveAfterDays] int NULL,
    [AutoDeleteAfterDays] int NULL,
    [IsEnabled] bit NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_RetentionPolicies] PRIMARY KEY ([ID])
);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260218135617_AddRetentionNoticeSent', N'10.0.3');

COMMIT;
GO

