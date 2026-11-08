# Triage Labels

The skills speak in terms of five canonical triage roles. This file maps those roles to the actual label strings used in this repo's issue tracker.

| Label in mattpocock/skills | Label in our tracker | Meaning                                  |
| -------------------------- | -------------------- | ---------------------------------------- |
| `needs-triage`             | `needs-triage`       | Maintainer needs to evaluate this issue  |
| `needs-info`               | `needs-info`         | Waiting on reporter for more information |
| `ready-for-agent`          | `ready-for-agent`    | Fully specified, ready for an AFK agent  |
| `ready-for-human`          | `ready-for-human`    | Requires human implementation            |
| `wontfix`                  | `wontfix`            | Will not be actioned                     |

When a skill mentions a role (e.g. "apply the AFK-ready triage label"), use the corresponding label string from this table.

Edit the right-hand column to match whatever vocabulary you actually use.

## Repo facts

The defaults were kept as-is: each of the five label strings equals its role name, nothing renamed.

The tracker is local markdown files (see [issue-tracker.md](issue-tracker.md)), so "apply a label"
here means writing the `Status:` line at the top of the ticket file with the string from the
right-hand column. If a different vocabulary is adopted later — for instance aligning with the
wording of a prototype's 待确认区 in design.md — changing the right-hand column is enough; the skills need no edit.
