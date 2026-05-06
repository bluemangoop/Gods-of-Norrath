# Changelog

## [1.0.0.4] 2026-05-06

- Removed manifest.json dependency entirely
- Patcher now downloads static files unconditionally from hardcoded URLs
- Whitelist: BaseData.txt, SkillCaps.txt, GlobalLoad.txt, dbstr_us.txt, spells_us.txt
- Files are placed in correct Resources/ subdirectory automatically

## [1.0.0.3] 2026-05-06

- Replaced xxhash64 checksum system with file size + mtime comparison
- Server-side manifest generator now records {size, mtime} instead of xxhash64 hashes
- Client-side patcher compares FileInfo.Length and LastWriteTimeUtc against manifest values
- Fixed checksum mismatch bug where files always re-downloaded or never updated


## [1.0.0.2] 2026-05-06

- Updated manifest URL to use domain (godsofnorrath.online) instead of raw IP
- Updated GitHub Actions build URLs to use domain
- Added GlobalLoad.txt to patcher whitelist

## [1.0.0.1] 2026-05-06

- Patcher now downloads manifest from web server instead of GitHub
- Manifest auto-generated every 30 seconds with live xxhash64 hashes
- Fixed CRC32 collision bug in export_watcher (replaced with MD5)

## [1.0.4] 2023-02-11

- Introduced new CICD-friendly pipeline
- Added self updating support (yay)
- Added HTTPS support
- Fixed background threading and context cancellation, so it runs smoother (and cancels smoother)
- Added progress in the taskbar icon
- Added option to point to your self built hosting inside the CICD
- Added a pipeline that makes it easy to pull down upstream changes without impacting your version 
- Fix virustotal from falsely identifying as virus
