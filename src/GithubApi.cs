using System.Text;
using Newtonsoft.Json.Linq;
using Sodium;

namespace SecretsMigrator
{
    public class GithubApi
    {
        private readonly GithubClient _client;
        private readonly string _apiUrl;

        public GithubApi(GithubClient client, string apiUrl)
        {
            _client = client;
            _apiUrl = apiUrl;
        }

        public virtual async Task<string> GetDefaultBranch(string org, string repo)
        {
            var url = $"{_apiUrl}/repos/{org}/{repo}";

            var response = await _client.GetAsync(url);
            var data = JObject.Parse(response);

            return (string)data["default_branch"];
        }

        public virtual async Task CreateBranch(string org, string repo, string branchName, string sha)
        {
            var url = $"{_apiUrl}/repos/{org}/{repo}/git/refs";

            var payload = new
            {
                @ref = $"refs/heads/{branchName}",
                sha
            };

            var response = await _client.PostAsync(url, payload);
            _ = JObject.Parse(response);
        }

        public virtual async Task<string> GetCommitSha(string org, string repo, string branch)
        {
            var url = $"{_apiUrl}/repos/{org}/{repo}/git/ref/heads/{branch}";

            var response = await _client.GetAsync(url);
            var data = JObject.Parse(response);

            return (string)data["object"]["sha"];
        }

        public virtual async Task CreateRepoSecret(string org, string repo, byte[] publicKey, string publicKeyId, string secretName, string secretValue)
        {
            var url = $"{_apiUrl}/repos/{org}/{repo}/actions/secrets/{secretName}";

            var secretBytes = Encoding.UTF8.GetBytes(secretValue);

            var sealedPublicKeyBox = SealedPublicKeyBox.Create(secretBytes, publicKey);
            var encryptedSecret = Convert.ToBase64String(sealedPublicKeyBox);

            var payload = new
            {
                encrypted_value = encryptedSecret,
                key_id = publicKeyId
            };

            await _client.PutAsync(url, payload);
        }

        public virtual async Task<(byte[] publicKey, string publicKeyId)> GetRepoPublicKey(string org, string repo)
        {
            var url = $"{_apiUrl}/repos/{org}/{repo}/actions/secrets/public-key";

            var response = await _client.GetAsync(url);
            var data = JObject.Parse(response);

            var publicKey64 = (string)data["key"];
            var publicKeyBytes = Convert.FromBase64String(publicKey64);

            return (publicKeyBytes, (string)data["key_id"]);
        }

        public virtual async Task<string> CreateBlob(string org, string repo, string contents)
        {
            var url = $"{_apiUrl}/repos/{org}/{repo}/git/blobs";

            var payload = new
            {
                content = Convert.ToBase64String(Encoding.UTF8.GetBytes(contents)),
                encoding = "base64"
            };

            var response = await _client.PostAsync(url, payload);
            var data = JObject.Parse(response);

            return (string)data["sha"];
        }

        public virtual async Task<string> GetTreeSha(string org, string repo, string commitSha)
        {
            var url = $"{_apiUrl}/repos/{org}/{repo}/git/commits/{commitSha}";

            var response = await _client.GetAsync(url);
            var data = JObject.Parse(response);

            return (string)data["tree"]["sha"];
        }

        public virtual async Task CreateFile(string org, string repo, string branch, string filePath, string contents)
        {
            var url = $"{_apiUrl}/repos/{org}/{repo}/contents/{filePath}";

            var payload = new
            {
                message = "Creating migrate secrets workflow",
                content = Convert.ToBase64String(Encoding.UTF8.GetBytes(contents)),
                branch
            };

            await _client.PutAsync(url, payload);
        }

        public virtual async Task<string> CreateTreeFromBlob(string org, string repo, string blobSha, string filename, string previousTreeSha)
        {
            var url = $"{_apiUrl}/repos/{org}/{repo}/git/trees";

            var payload = new
            {
                base_tree = previousTreeSha,
                tree = new[]
                {
                    new
                    {
                        path = filename,
                        mode = "100644",
                        type = "blob",
                        sha = blobSha
                    }
                }
            };

            var response = await _client.PostAsync(url, payload);
            var data = JObject.Parse(response);

            return (string)data["sha"];
        }

        public virtual async Task<string> CreateTreeFromBlob(string org, string repo, string blobSha, string filename)
        {
            var url = $"{_apiUrl}/repos/{org}/{repo}/git/trees";

            var payload = new
            {
                tree = new[]
                {
                    new
                    {
                        path = filename,
                        mode = "100644",
                        type = "blob",
                        sha = blobSha
                    }
                }
            };

            var response = await _client.PostAsync(url, payload);
            var data = JObject.Parse(response);

            return (string)data["sha"];
        }

        public virtual async Task UpdateBranch(string org, string repo, string branchName, string commitSha)
        {
            var url = $"{_apiUrl}/repos/{org}/{repo}/git/ref/heads/{branchName}";

            var payload = new
            {
                sha = commitSha,
                force = true
            };

            await _client.PatchAsync(url, payload);
        }

        public virtual async Task<string> CreateCommit(string org, string repo, string treeSha, string parentCommitSha, string message)
        {
            var url = $"{_apiUrl}/repos/{org}/{repo}/git/commits";

            var payload = new
            {
                message,
                parents = new[]
                {
                    parentCommitSha
                },
                tree = treeSha
            };

            var response = await _client.PostAsync(url, payload);
            var data = JObject.Parse(response);

            return (string)data["sha"];
        }

        public virtual async Task<string> CreateTreeFromTree(string org, string repo, string treeSha, string folderName)
        {
            var url = $"{_apiUrl}/repos/{org}/{repo}/git/trees";

            var payload = new
            {
                tree = new[]
                {
                    new
                    {
                        path = folderName,
                        mode = "040000",
                        type = "tree",
                        sha = treeSha
                    }
                }
            };

            var response = await _client.PostAsync(url, payload);
            var data = JObject.Parse(response);

            return (string)data["sha"];
        }

        public virtual async Task<string> CreateTreeFromTree(string org, string repo, string treeSha, string folderName, string previousTreeSha)
        {
            var url = $"{_apiUrl}/repos/{org}/{repo}/git/trees";

            var payload = new
            {
                base_tree = previousTreeSha,
                tree = new[]
                {
                    new
                    {
                        path = folderName,
                        mode = "040000",
                        type = "tree",
                        sha = treeSha
                    }
                }
            };

            var response = await _client.PostAsync(url, payload);
            var data = JObject.Parse(response);

            return (string)data["sha"];
        }

        public virtual async Task<string> GetLatestRunBranchWorkflow(string org, string repo, string branch, string workflowId)
        {
            var url = $"{_apiUrl}/repos/{org}/{repo}/actions/workflows/{workflowId}/runs?branch={branch}";
            var data = JObject.Parse(@"{""total_count"": 0}");

            var retryCount = 0;
            while (((int)data["total_count"]).Equals(0) && retryCount < 20)
            {
                await Task.Delay(1000); // start with delay for job to initialize
                Console.WriteLine($"Retry: {retryCount}");
                var response = await _client.GetAsync(url);
                data = JObject.Parse(response);
                retryCount++;
            }

            var runId = data["workflow_runs"].OrderByDescending(x => x["run_number"]).First()["id"];

            return (string)runId;
        }

        public virtual async Task<(string status, string conclusion)> GetWorkflowRunStatus(string org, string repo, string runId)
        {
            var url = $"{_apiUrl}/repos/{org}/{repo}/actions/runs/{runId}";

            var response = await _client.GetAsync(url);
            var data = JObject.Parse(response);

            return ((string)data["status"], (string)data["conclusion"]);
        }
    }
}
