using System.Net.Http;

namespace SecretsMigrator
{
    public class GithubApiFactory
    {
        private const string DEFAULT_API_URL = "https://api.github.com";

        private readonly OctoLogger _octoLogger;
        private readonly HttpClient _client;

        public GithubApiFactory(OctoLogger octoLogger, HttpClient client)
        {
            _octoLogger = octoLogger;
            _client = client;
        }

        public virtual GithubApi Create(string personalAccessToken)
        {
            var githubClient = new GithubClient(_octoLogger, _client, personalAccessToken);
            return new GithubApi(githubClient, DEFAULT_API_URL);
        }
    }
}
