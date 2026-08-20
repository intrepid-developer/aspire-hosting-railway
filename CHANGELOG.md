# Changelog

Versions match `Directory.Build.props`. Preview packages are on GitHub Packages, not nuget.org. This file starts at **0.1.0-preview.11**. Earlier previews are not listed here.

## 0.1.0-preview.12

- Adopt existing Railway buckets by name from `project(id).buckets` on deploy so CI and new machines do not `bucketCreate` a second undeployed bucket (`BucketInstance not found`).
- After a real `bucketCreate`, retry `bucketS3Credentials` until a BucketInstance exists instead of querying immediately.
- A same-name service is never used as a `bucketId`.
- Bucket secrets stay out of plan files and deployment state; only flatten-safe bucket ids are persisted.

## 0.1.0-preview.11

- Previous published preview on GitHub Packages.
- Empty optional captured parameters are omitted on `aspire deploy` instead of aborting. Missing required parameters still fail.
