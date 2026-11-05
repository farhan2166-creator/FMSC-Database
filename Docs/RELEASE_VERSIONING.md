# One-place application versioning

The **only application version to edit** for an approved new release is the
`$AppVersion` assignment in [`Installer/core.ps1`](../Installer/core.ps1).

For an application-only update from 1.0.0 to 1.0.1, change:

```powershell
$AppVersion = '1.0.1'
$DatabaseVersion = '1'
```

`FMSC_SMS.csproj` reads the application version directly from the installer
script when MSBuild evaluates the project. Its three release properties are
derived, not separately maintained:

- `Version` — `1.0.1` (private update package and About metadata).
- `AssemblyVersion` — `1.0.1.0` (.NET assembly identity).
- `FileVersion` — `1.0.1.0` (Windows file metadata).

The existing publisher compares the built DLL, the bundled installer and its
update manifest, then verifies the signed-off package before advancing the
private stable channel. Publish from `main` using the owner-initiated
**04 - Build and Publish Private Update** workflow. A previously published
version cannot be republished; increase `$AppVersion` before the next
approved update.

## Database versions are independent

Do **not** increase `$DatabaseVersion` for UI or application-only changes.
Database version **1** remains correct until an approved database migration is
required. A database change needs `Migrations/2.sql` (and consecutive scripts
after it), the application schema requirement in `DatabaseHelper.cs`, and
matching compatibility/fixture checks. Those are intentional compatibility
changes, not an automatic side-effect of bumping an application version.

**Published 1.0.0 is immutable.** This versioning convenience makes no release
or database change by itself. Test and approve every new version before
publishing it.
