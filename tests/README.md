# Unit tests

The unit tests included here utilize `NUnit` and `Moq`.

Tested functions:
- `AdminController.GetPreventSleepSetting`: Returns the server's prevent sleep setting state.
- `AdminController.ReindexAll`: Triggers a full re-index of the search DB correctly.
- `MediaController.GetFile` and `MediaController.DeleteFile`: Basic validation to ensure invalid requests return properly.
- `ProcessController.GetClientSettings` and `ProcessController.PostProcess`: Asserts configuration values return properly and bad empty transcripts fail early.
- `ConversationsController.GetConversations` and `ConversationsController.DeleteConversation`: Exercises DB wrapper methods are properly called.
- TaskHandlers (`TimeTaskHandler`, `ShowRemindersTaskHandler`, `PrintTaskHandler`, `AdminPanelTaskHandler`): Asserts the commands/keywords are correct and that specific strings or commands are output correctly.
- `FullComponentTests.ProcessSpeech`: Triggers the entire stack from the `ProcessController`, routing speech transcript through a mocked Bedrock API layer `IAIService` down into returning the proper controller payload.
