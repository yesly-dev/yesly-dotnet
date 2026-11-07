# Publishing to NuGet

The package is `Yesly` (`dotnet add package Yesly`). Releases are published by
`.github/workflows/release.yml` when a `v*` tag is pushed. It uses NuGet
trusted publishing (OIDC from GitHub Actions through `NuGet/login`), so no
long-lived API key is stored.

## One-time setup

1. **nuget.org account.** Sign in at <https://www.nuget.org> with a Microsoft
   account on a shared address and turn on two-factor authentication.
2. **Organisation.** *Manage organizations* → *Add new*, for example `Yesly`.
   Packages pushed under it are owned by the organisation, not by one person.
3. **Trusted publishing policy.** Go to *Trusted Publishing* (in the account
   menu) → *Create*, owned by the organisation:
   - Repository owner: `yesly-dev`
   - Repository: `yesly-dotnet`
   - Workflow file: `release.yml`
   - Environment: `nuget`
4. **GitHub.** In the repo, *Settings* → *Environments* → create `nuget`, and
   add the secret `NUGET_USER` with the nuget.org **user name** of the account
   from step 1. It is not an email or a key; `NuGet/login` trades the OIDC
   token for a short-lived key for that user.
5. **Optional: reserve the `Yesly` prefix.** Ask through *Contact support* on
   nuget.org. It puts the "verified" tick on `Yesly` and `Yesly.*` and stops
   others publishing under that prefix. NuGet asks for a public site and repo,
   so request it once both are public.

On 2026-11-07, the ID `Yesly` was free. The first push creates it.

## Each release

1. Bump `<Version>` in `src/Yesly/Yesly.csproj` and `YeslyVersion.Current` in
   `src/Yesly/Version.cs`.
2. Merge to `main`.
3. Tag and push:

   ```bash
   git tag v0.2.0 && git push origin v0.2.0
   ```

The workflow checks the tag against the csproj, runs the tests, packs
`.nupkg` and `.snupkg` (symbols), and pushes both. NuGet validates and indexes
the package in a few minutes. Check <https://www.nuget.org/packages/Yesly>.

## If something goes wrong

- A version cannot be replaced. Fix forward with a new version.
- *Unlist* a bad version on its nuget.org page. It stops showing in search and
  in new installs, and existing references keep restoring.
