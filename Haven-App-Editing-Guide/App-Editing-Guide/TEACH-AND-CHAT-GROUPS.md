# Teach and Chat Groups

## Exact conversation scopes

`ConversationScope` in `src/Haven.Core/Entities.cs` is the canonical model for list and history boundaries:

| Scope | Required persistence |
|---|---|
| General Chat | `Mode = Chat`, `Kind = Chat`, no container or lesson |
| Chat Group | `Mode = Chat`, `Kind = Chat`, exact `ContainerId`, no lesson |
| Teach Quick Chat | `Mode = Teach`, `Kind = QuickChat`, no container or lesson |
| Teach Lesson | `Mode = Teach`, `Kind = LessonChat`, exact subject and lesson IDs |

Use `ConversationScope.GeneralChat`, `TeachQuickChat`, `ForChatGroup(id)` or `ForTeachLesson(subjectId, lessonId)`. Do not reproduce these predicates in a view-model.

`ConversationRepository.GetRecentInScopeAsync` applies the exact SQL predicate and the scoped migration index. Do not return to loading a mode-wide recent list and filtering it in memory; that loses older group/lesson chats and mixes scopes.

## Teach lifecycle

Teach uses the shared `ChatPageViewModel`, but switching to Teach does not select the first subject or lesson. Quick Chats always remain available outside subjects. The sidebar always exposes Quick Chats, Subjects and Create Subject, plus an explanatory empty state when appropriate.

Subject creation goes through `IContainerRepository.CreateSubjectAsync`. It inserts the subject and its default `General` lesson in one SQLite transaction. Lessons are ordered by `SortOrder` and carry `TopicGroup`, `Name` and `StructureJson`.

Rapid subject switching cancels the previous lesson load. Keep the cancellation token through the repository call and check that the selected subject still matches before replacing the observable collection.

The model context for a lesson is composed in `ChatPageViewModel` and includes:

- subject context and instructions;
- subject name;
- lesson name;
- topic group;
- lesson structure JSON.

Quick Chats must not inherit a subject or lesson context. Deleting a lesson clears its lesson link and converts its conversations into Teach Quick Chats; messages remain intact.

## Chat Group home

A Chat Group is a `ContainerDefinition` with `Mode = Chat` and no local project root. Opening a group selects its stable group-home tab and activates that group without changing the IDs of existing grouped conversations.

`ChatGroupPageViewModel` owns:

- New Chat;
- recent and pinned group chat statistics;
- group context and model instructions;
- durable reference files;
- Settings;
- Archive;
- confirmed permanent deletion.

Each group keeps an independent current chat in `MainWindowViewModel`; opening another group must not reuse one mutable group conversation.

Archive updates `ContainerDefinition.IsArchived` and preserves chats and resources. Confirmed permanent deletion calls `DeleteAndDetachConversationsAsync`: group conversations become General Chat conversations and their messages are retained. Never cascade-delete conversation history as part of deleting a group.

## Durable reference files

`ContainerResourceRepository` validates, hashes and copies an accepted file into:

```text
%APPDATA%\Haven\container-resources\<container-id-N>\<generated-stored-name>
```

The root changes with `HAVEN_DATA_DIR`. Metadata lives in `container_resources`. A unique `(container_id, sha256)` index deduplicates the same content inside one group.

Accepted input currently covers text/code/Markdown/JSON/CSV, PDF, DOCX and images. The repository enforces type and size limits before copying, writes through a temporary file, and removes a failed partial copy. Text and DOCX contents can be added to the group system context within a bounded character budget; image files are attached only when the selected model path supports images. PDF metadata is retained but PDF text extraction is not performed by this repository.

Do not read a reference from its original source path after import. `StoredName` must remain a filename, and `GetStoredPath` confines reads to the group's data directory.

## Tool boundary

Chat Group context is shared model context, not a workspace. A group must not receive:

- project file read/write/search tools;
- Git actions;
- build/test commands;
- arbitrary command execution;
- Studio project intelligence.

Those require Do/Studio plus an existing selected root and the matching permission. Add a tool-policy regression test whenever group context is threaded through a new model request.

## Required tests

The focused suites are:

- `tests/Haven.Core.Tests/ConversationScopeTests.cs`;
- `tests/Haven.Infrastructure.Tests/TeachAndChatGroupRepositoryTests.cs`.

Cover a fresh database, exact scope isolation, atomic subject/default-lesson creation, rapid cancellation in UI/headless tests, restart persistence, full lesson context, file validation/deduplication, archive preservation and detach-on-delete behavior.
