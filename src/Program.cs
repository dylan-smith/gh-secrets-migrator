using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;

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
            root.AddOption(verbose);

            root.Handler = CommandHandler.Create<string, string, string, string, string, string, bool>(Invoke);

            await root.InvokeAsync(args);
        }

        public static async Task Invoke(string sourceOrg, string sourceRepo, string targetOrg, string targetRepo, string sourcePat, string targetPat, bool verbose = false)
        {
            _log.Verbose = verbose;

            _log.LogInformation("Migrating Secrets...");
            _log.LogInformation($"SOURCE ORG: {sourceOrg}");
            _log.LogInformation($"SOURCE REPO: {sourceRepo}");
            _log.LogInformation($"TARGET ORG: {targetOrg}");
            _log.LogInformation($"TARGET REPO: {targetRepo}");

            var branchName = "migrate-secrets";
            var workflow = GenerateWorkflow(targetOrg, targetRepo, targetPat, branchName);

            var githubClient = new GithubClient(_log, sourcePat);
            var githubApi = new GithubApi(githubClient, "https://api.github.com");

            var (publicKey, publicKeyId) = await githubApi.GetRepoPublicKey(sourceOrg, sourceRepo);
            await githubApi.CreateRepoSecret(sourceOrg, sourceRepo, publicKey, publicKeyId, "SECRETS_MIGRATOR_PAT", targetPat);

            var defaultBranch = await githubApi.GetDefaultBranch(sourceOrg, sourceRepo);
            var masterCommitSha = await githubApi.GetCommitSha(sourceOrg, sourceRepo, defaultBranch);
            await githubApi.CreateBranch(sourceOrg, sourceRepo, branchName, masterCommitSha);

            await githubApi.CreateFile(sourceOrg, sourceRepo, branchName, ".github/workflows/migrate-secrets.yml", workflow);

            _log.LogSuccess($"Secrets migration in progress. Check on status at https://github.com/{sourceOrg}/{sourceRepo}/actions");
        }

        private static string GenerateWorkflow(string targetOrg, string targetRepo, string targetPat, string branchName)
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
      - run: |
          Install-Package -Name Sodium.Core -ProviderName NuGet -Scope CurrentUser -RequiredVersion 1.3.0 -Destination . -Force
          $sodiumPath = Resolve-Path "".\Sodium.Core.1.3.0\lib\\netstandard2.1\Sodium.Core.dll""
          [System.Reflection.Assembly]::LoadFrom($sodiumPath)

          $targetPat = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("":$($env:TARGET_PAT)""))
          $publicKeyResponse = Invoke-RestMethod -Uri ""https://api.github.com/repos/$env:TARGET_ORG/$env:TARGET_REPO/actions/secrets/public-key"" -Method ""GET"" -Headers @{{ Authorization = ""Basic $targetPat"" }}
          $publicKey = [Convert]::FromBase64String($publicKeyResponse.key)
          $publicKeyId = $publicKeyResponse.key_id
              
          $secrets = $env:REPO_SECRETS | ConvertFrom-Json
          $secrets | Get-Member -MemberType NoteProperty | ForEach-Object {{
            $secretName = $_.Name
            $secretValue = $secrets.""$secretName""
     
            if ($secretName -ne ""github_token"" -and $secretName -ne ""SECRETS_MIGRATOR_PAT"") {{
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

          Invoke-RestMethod -Uri ""https://api.github.com/repos/${{{{ github.repository }}}}/git/${{{{ github.ref }}}}"" -Method ""DELETE"" -Headers @{{ Authorization = ""Basic $targetPat"" }}
        env:
          REPO_SECRETS: ${{{{ toJSON(secrets) }}}}
          TARGET_PAT: ${{{{ secrets.SECRETS_MIGRATOR_PAT }}}}
          TARGET_ORG: '{targetOrg}'
          TARGET_REPO: '{targetRepo}'
        shell: pwsh
";

            return result;
        }
    }
}
