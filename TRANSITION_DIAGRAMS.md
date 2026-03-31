# Smart Glasses Middleware - Transition Diagrams

This document contains Mermaid.js state transition diagrams that model the different main sections (referred to as pages) and features of the project: Voice Interface, Files, Conversations (Prompts), Reminders, and Groups.

## System Overview

The application is split into two primary front-end interfaces:
1. **The Voice Client (`App.jsx`)**: The primary interface on the smart glasses, handling voice activation, listening, processing, and rendering documents/reminders.
2. **The Admin Panel (`admin.html`)**: A web-based dashboard used to manage files, conversations, reminders, and groups.

---

## 1. Voice Interface (Smart Glasses Client)

This diagram shows the state transitions of the voice pipeline managed in `App.jsx`, including handling wake words and command execution (e.g., opening files, showing reminders).

```mermaid
stateDiagram-v2
    [*] --> LandingScreen : Initial Load (No Activation)

    LandingScreen --> Idle : User says Activation Word
    Idle --> Listening : Wake Word Detected (e.g. "Activate")

    Listening --> Processing : Silence detected (End of Speech)
    Processing --> Sending : Transcription complete (Local or Cloud API)

    Sending --> Idle : Command Executed (Update UI)
    Sending --> Error : Network/Transcription Failure
    Error --> Idle : Timeout / Dismiss

    state CommandExecution {
        Idle --> ShowRecentFiles : "Search recent files"
        Idle --> ShowReminders : "Show my reminders"
        Idle --> OpenPDF : "Select document <name>" / "Number X"
        Idle --> AdminPanel : "Open admin panel"

        ShowRecentFiles --> OpenPDF : Selects a file
        OpenPDF --> CloseDocument : "Close document"
        CloseDocument --> Idle
        ShowReminders --> Idle : Auto-fade out
    }
```

---

## 2. Admin Panel Navigation

The Admin Panel operates as a Single Page Application (SPA) where the "pages" are represented as top-level tabs.

```mermaid
stateDiagram-v2
    [*] --> AdminDashboard

    state AdminDashboard {
        [*] --> FilesTab : Default View

        FilesTab --> ConversationsTab : Click "Conversations"
        FilesTab --> RemindersTab : Click "Reminders"
        FilesTab --> GroupsTab : Click "Groups"

        ConversationsTab --> FilesTab : Click "Files"
        ConversationsTab --> RemindersTab : Click "Reminders"
        ConversationsTab --> GroupsTab : Click "Groups"

        RemindersTab --> FilesTab : Click "Files"
        RemindersTab --> ConversationsTab : Click "Conversations"
        RemindersTab --> GroupsTab : Click "Groups"

        GroupsTab --> FilesTab : Click "Files"
        GroupsTab --> ConversationsTab : Click "Conversations"
        GroupsTab --> RemindersTab : Click "Reminders"
    }
```

---

## 3. Files Tab

Transitions and interactions within the **Files** section.

```mermaid
stateDiagram-v2
    [*] --> FilesList

    FilesList --> UploadFile : Click "Upload Document"
    UploadFile --> UploadingState : Select File
    UploadingState --> FilesList : Upload Success
    UploadingState --> ErrorState : Upload Failed

    FilesList --> FilePreviewModal : Click on File Thumbnail / "Preview"
    FilePreviewModal --> FilesList : Close Modal

    FilesList --> DeleteConfirmation : Click "Delete"
    DeleteConfirmation --> FilesList : Confirm / Cancel
```

---

## 4. Conversations (Prompts) Tab

Transitions and interactions within the **Conversations** section.

```mermaid
stateDiagram-v2
    [*] --> ConversationsList

    ConversationsList --> EditConversation : Click "Edit"
    EditConversation --> ConversationsList : Save / Cancel

    ConversationsList --> ConversationView : Select Conversation
    ConversationView --> AttachmentPanel : Click "Attach file" (📎)
    AttachmentPanel --> FileUpload : Upload File to Prompt
    FileUpload --> AttachmentPanel : Upload Complete
    AttachmentPanel --> ConversationView : Close Panel

    ConversationsList --> DeleteConfirmation : Click "Delete"
    DeleteConfirmation --> ConversationsList : Confirm / Cancel
```

---

## 5. Reminders Tab

Transitions and interactions within the **Reminders** section.

```mermaid
stateDiagram-v2
    [*] --> RemindersList

    RemindersList --> CreateReminder : Submit Reminder Form
    CreateReminder --> RemindersList : Success / Update

    RemindersList --> AttachmentPanel : Click Paperclip (📎)
    AttachmentPanel --> FileUpload : Upload File to Reminder
    FileUpload --> AttachmentPanel : Upload Complete
    AttachmentPanel --> RemindersList : Close Panel

    RemindersList --> ReminderPreviewModal : View Attached File
    ReminderPreviewModal --> RemindersList : Close Modal

    RemindersList --> DeleteConfirmation : Click "Delete"
    DeleteConfirmation --> RemindersList : Confirm / Cancel
```

---

## 6. Groups Tab

Transitions and interactions within the **Groups** section. Used to group multiple resources (Prompts, Files, and Reminders) together.

```mermaid
stateDiagram-v2
    [*] --> GroupsList

    GroupsList --> CreateGroup : Submit Group Form
    CreateGroup --> GroupsList : Success / Update

    GroupsList --> ManageGroupModal : Click "Manage Group"
    ManageGroupModal --> ManageFiles : Switch to Files Tab (Modal)
    ManageGroupModal --> ManagePrompts : Switch to Prompts Tab (Modal)
    ManageGroupModal --> ManageReminders : Switch to Reminders Tab (Modal)
    ManageGroupModal --> GroupsList : Close Modal

    GroupsList --> EditGroup : Click "Edit"
    EditGroup --> GroupsList : Save / Cancel

    GroupsList --> DeleteConfirmation : Click "Delete"
    DeleteConfirmation --> GroupsList : Confirm / Cancel
```
