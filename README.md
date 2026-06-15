# AppMare Builds

Single permanent CI repo for all AppMare app builds.
Each build runs in an isolated GitHub Environment — secrets never mix between builds.

## Structure
- `.github/workflows/build.yml` — minimal wrapper (3 jobs: android, apple, windows)
- `.github/scripts/build.sh` — all build logic (auto-detects .NET version from csproj)
