## What and why

<!-- What does this change do, and which problem does it solve? Link the issue: Closes #… -->

## How it was checked

- [ ] `dotnet format`, build (Release) and tests pass locally
- [ ] New behaviour has tests (unit, architecture or API integration)
- [ ] New endpoints use the narrowest staff policy (`operate`, `care`, `finance`, `administer`) and `RoleAccessTests` stays green
- [ ] Screens checked in Arabic and English, dark and light, and at phone width (if UI changed)
- [ ] Texts added to both `.resx` and `.ar.resx` (if UI changed)
- [ ] Migration added and `has-pending-model-changes` is clean (if the database model changed)
- [ ] Docs / ADRs updated (if a decision or behaviour changed)

## Screenshots

<!-- For UI changes: before / after, Arabic and English. No real customer data. -->
