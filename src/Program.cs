using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System;

namespace SecretsMigrator
{
    public static class Program
    {
        private static readonly OctoLogger _log = new();

        public static async Task Main(string[] args)
        {
            var root = new RootCommand
            {
                Description = "Migrates all secrets from one GitHub repo to another."
            };

            var sourceOrg = new Option<string>("--source-org")
            {
                IsRequired = true
            };
            var sourceRepo = new Option<string>("--source-repo")
            {
                IsRequired = true
            };
            var targetOrg = new Option<string>("--target-org")
            {
                IsRequired = true
            };
            var targetRepo = new Option<string>("--target-repo")
            {
                IsRequired = true
            };
            var sourcePat = new Option<string>("--source-pat")
            {
                IsRequired = true
            };
            var targetPat = new Option<string>("--target-pat")
            {
                IsRequired = true
            };
            var ignoreOrgSecrets = new Option("--ignore-org-secrets")
            {
                IsRequired = false
            };
            var verbose = new Option("--verbose")
            {
                IsRequired = false
            };

            root.AddOption(sourceOrg);
            root.AddOption(sourceRepo);
            root.AddOption(targetOrg);
            root.AddOption(targetRepo);
            root.AddOption(sourcePat);
            root.AddOption(targetPat);
            root.AddOption(ignoreOrgSecrets);
            root.AddOption(verbose);

            root.Handler = CommandHandler.Create<string, string, string, string, string, string, bool, bool>(Invoke);

            await root.InvokeAsync(args);
        }

        public static async Task Invoke(string sourceOrg, string sourceRepo, string targetOrg, string targetRepo, string sourcePat, string targetPat, bool ignoreOrgSecrets = false, bool verbose = false)
        {
            _log.Verbose = verbose;

            _log.LogInformation("Migrating Secrets...");
            _log.LogInformation($"SOURCE ORG: {sourceOrg}");
            _log.LogInformation($"SOURCE REPO: {sourceRepo}");
            _log.LogInformation($"TARGET ORG: {targetOrg}");
            _log.LogInformation($"TARGET REPO: {targetRepo}");

            var id = (new Random().Next(1000, 9999)).ToString();
            var branchName = $"migrate-secrets-{id}";
            var workflow = GenerateWorkflow(sourceOrg, sourceRepo, targetOrg, targetRepo, branchName, ignoreOrgSecrets);

            var githubClient = new GithubClient(_log, sourcePat);
            var githubApi = new GithubApi(githubClient, "https://api.github.com");

            var (publicKey, publicKeyId) = await githubApi.GetRepoPublicKey(sourceOrg, sourceRepo);
            await githubApi.CreateRepoSecret(sourceOrg, sourceRepo, publicKey, publicKeyId, "SECRETS_MIGRATOR_TARGET_PAT", targetPat);
            await githubApi.CreateRepoSecret(sourceOrg, sourceRepo, publicKey, publicKeyId, "SECRETS_MIGRATOR_SOURCE_PAT", sourcePat);

            var defaultBranch = await githubApi.GetDefaultBranch(sourceOrg, sourceRepo);
            var masterCommitSha = await githubApi.GetCommitSha(sourceOrg, sourceRepo, defaultBranch);
            await githubApi.CreateBranch(sourceOrg, sourceRepo, branchName, masterCommitSha);

            await githubApi.CreateFile(sourceOrg, sourceRepo, branchName, ".github/workflows/migrate-secrets.yml", workflow);

            _log.LogSuccess($"Secrets migration in progress. Check on status at https://github.com/{sourceOrg}/{sourceRepo}/actions");
        }

        private static string GenerateWorkflow(string sourceOrg, string sourceRepo, string targetOrg, string targetRepo, string branchName, bool ignoreOrgSecrets = false)
        {
            var result = $@"
name: move-secrets
on:
  push:
    branches: [ ""{branchName}"" ]
jobs:
  build:
    runs-on: windows-latest
    steps:
      - name: Install Crypto Package
        run: |
          Install-Package -Name Sodium.Core -ProviderName NuGet -Scope CurrentUser -RequiredVersion 1.3.0 -Destination . -Force
        shell: pwsh
      - name: Migrate Secrets
        run: |
          $sodiumPath = Resolve-Path "".\Sodium.Core.1.3.0\lib\\netstandard2.1\Sodium.Core.dll""
          [System.Reflection.Assembly]::LoadFrom($sodiumPath)

          $targetPat = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("":$($env:TARGET_PAT)""))
          $sourcePat = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("":$($env:SOURCE_PAT)""))
          $publicKeyResponse = Invoke-RestMethod -Uri ""https://api.github.com/repos/$env:TARGET_ORG/$env:TARGET_REPO/actions/secrets/public-key"" -Method ""GET"" -Headers @{{ Authorization = ""Basic $targetPat"" }}
          $publicKey = [Convert]::FromBase64String($publicKeyResponse.key)
          $publicKeyId = $publicKeyResponse.key_id

          $secrets = $env:REPO_SECRETS | ConvertFrom-Json
          $ignoreSecrets = @(""github_token"", ""SECRETS_MIGRATOR_SOURCE_PAT"", ""SECRETS_MIGRATOR_TARGET_PAT"")

          if ([System.Convert]::ToBoolean($env:IGNORE_ORG_SECRETS)) {{
            $orgSecretsResponse = Invoke-RestMethod -Uri ""https://api.github.com/repos/$env:SOURCE_ORG/$env:SOURCE_REPO/actions/organization-secrets"" -Method ""GET"" -Headers @{{ Authorization = ""Basic $sourcePat"" }}
            $ignoreSecrets += $orgSecretsResponse.secrets.name
          }}
 
          $secrets | Get-Member -MemberType NoteProperty | ForEach-Object {{
            $secretName = $_.Name
            $secretValue = $secrets.""$secretName""
     
            if ($secretName -notin $ignoreSecrets) {{
              Write-Output ""Migrating Secret: $secretName""
              $secretBytes = [Text.Encoding]::UTF8.GetBytes($secretValue)
              $sealedPublicKeyBox = [Sodium.SealedPublicKeyBox]::Create($secretBytes, $publicKey)
              $encryptedSecret = [Convert]::ToBase64String($sealedPublicKeyBox)
                 
              $Params = @{{
                Uri = ""https://api.github.com/repos/$env:TARGET_ORG/$env:TARGET_REPO/actions/secrets/$secretName""
                Headers = @{{
                  Authorization = ""Basic $targetPat""
                }}
                Method = ""PUT""
                Body = ""{{ `""encrypted_value`"": `""$encryptedSecret`"", `""key_id`"": `""$publicKeyId`"" }}""
              }}

              $createSecretResponse = Invoke-RestMethod @Params
            }}
          }}

          Write-Output ""Cleaning up...""
          Invoke-RestMethod -Uri ""https://api.github.com/repos/${{{{ github.repository }}}}/git/${{{{ github.ref }}}}"" -Method ""DELETE"" -Headers @{{ Authorization = ""Basic $sourcePat"" }}
          Invoke-RestMethod -Uri ""https://api.github.com/repos/${{{{ github.repository }}}}/actions/secrets/SECRETS_MIGRATOR_TARGET_PAT"" -Method ""DELETE"" -Headers @{{ Authorization = ""Basic $sourcePat"" }}
          Invoke-RestMethod -Uri ""https://api.github.com/repos/${{{{ github.repository }}}}/actions/secrets/SECRETS_MIGRATOR_SOURCE_PAT"" -Method ""DELETE"" -Headers @{{ Authorization = ""Basic $sourcePat"" }}
        env:
          REPO_SECRETS: ${{{{ toJSON(secrets) }}}}
          TARGET_PAT: ${{{{ secrets.SECRETS_MIGRATOR_TARGET_PAT }}}}
          SOURCE_PAT: ${{{{ secrets.SECRETS_MIGRATOR_SOURCE_PAT }}}}
          TARGET_ORG: '{targetOrg}'
          TARGET_REPO: '{targetRepo}'
          SOURCE_ORG: '{sourceOrg}'
          SOURCE_REPO: '{sourceRepo}'
          IGNORE_ORG_SECRETS: '{ignoreOrgSecrets}'
        shell: pwsh
";

            return result;
        }
    }
}
