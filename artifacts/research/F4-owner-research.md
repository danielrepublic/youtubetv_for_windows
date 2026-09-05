# F4 Owner Research GitHub Repository Owner Verification
## Intent

Verify the owner/org for the public GitHub Releases source without inferring it
from the Windows username, local path, or project name.

## Finding: blocked

The local Git remote reports `https://github.com/danielrepublic/youtubetv_for_windows.git`,
which is a proposed remote value, not proof that this is the repository that will
host releases. Read-only checks of both the corresponding GitHub repository page
and GitHub REST repository endpoint returned 404. Therefore the actual public
release owner cannot be authoritatively identified from the available metadata.

`src/YouTubeTvShell/UpdateConfig.cs:11` still contains
`GitHubRepoOwner = "OWNER_PLACEHOLDER"`; it must remain unchanged. The updater
URL consequently remains a placeholder URL, and this F4 item is blocked.

## Exact required input

Provide the authoritative GitHub repository URL (or owner/org plus repository
name) that will host the public GitHub Release assets, and confirm that the
repository is reachable under that owner. Only then may the owner constant be
replaced and the release build re-run.

## Read-only sources and evidence

1. `git remote -v` — local origin reported
   `https://github.com/danielrepublic/youtubetv_for_windows.git`.
2. GitHub repository page:
   <https://github.com/danielrepublic/youtubetv_for_windows> — HTTP 404 at
   verification time.
3. GitHub REST endpoint:
   <https://api.github.com/repos/danielrepublic/youtubetv_for_windows> — HTTP
   404 at verification time.
4. `src/YouTubeTvShell/UpdateConfig.cs:10-18` — retains
   `OWNER_PLACEHOLDER` and derives the update URL from it.
5. `docs/RELEASE.md:33-49` and `docs/RELEASE-CHECKLIST.md:15-17` — require the
   real release owner and explicitly forbid inventing one.

No GitHub resource was created, edited, published, uploaded, or otherwise
mutated by this research.
