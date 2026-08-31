# PicMark release flow

A version tag triggers one build. The exact same artifacts are uploaded to Bitiful S4 before a GitHub Release is made public. The public domestic download root is `https://picmark-release.s3.bitiful.net/picmark`.

## Required GitHub repository secrets

- `S4_AK`: Bitiful S4 Access Key
- `S4_SK`: Bitiful S4 Secret Key
- `S4_BUCKET`: Bucket name only

Keep these values out of source code and logs. A dedicated Bitiful sub-user limited to this release bucket is recommended after the initial setup works.

## Publishing a version

1. Update `AssemblyVersion` and `AssemblyFileVersion` in `src/PicMark/Properties/AssemblyInfo.cs` to the target version, for example `0.3.1.0`.
2. Update `docs/update.json` with the same `latestVersion`, release links and notes.
3. Build and test locally, then commit the release changes.
4. Create and push a matching tag:

   ```powershell
   git tag v0.3.1
   git push origin v0.3.1
   ```

The workflow verifies that tag, assembly version and `docs/update.json` agree. It stops before publishing if they differ.

## S4 layout

```text
picmark/releases/v0.3.1/PicMark-Setup-0.3.1.exe
picmark/releases/v0.3.1/PicMark-portable-0.3.1.zip
picmark/releases/v0.3.1/SHA256SUMS.txt
picmark/releases/v0.3.1/release-manifest.json
picmark/update.json
```

The release workflow uploads every release artifact to Bitiful S4, then uses `s3api head-object` to read each object back and verify its byte size. It generates `picmark/update.json` only after that verification; this mirror manifest contains direct domestic URLs for the setup and portable packages. It writes an **S4 upload verified** section into the Actions summary and only creates the GitHub Release after every verification succeeds.

To receive a success signal without opening the bucket, enable repository release notifications in GitHub: **Watch > Custom > Releases**. A new PicMark Release then means the S4 upload and read-back verification both succeeded. If S4 upload or verification fails, the workflow fails and no GitHub Release is created.

The public download domain must expose only release files. Keep the bucket write credentials private, use a dedicated least-privilege S4 sub-user, and apply domain-side rate limits where available. The app itself uses GitHub first and reaches this mirror only after GitHub fails; the per-device fallback limit is stored locally and is three attempts per calendar day.
