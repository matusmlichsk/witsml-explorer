using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

using Witsml;
using Witsml.Data;

using WitsmlExplorer.Api.Configuration;
using WitsmlExplorer.Api.Models;
using WitsmlExplorer.Api.Repositories;

namespace WitsmlExplorer.Api.Services
{

    // ReSharper disable once UnusedMember.Global
    public class CredentialsService : ICredentialsService
    {
        private readonly ITimeLimitedDataProtector _dataProtector;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<CredentialsService> _logger;
        private readonly WitsmlClientCapabilities _clientCapabilities;
        private readonly IWitsmlSystemCredentials _witsmlServerCredentials;
        private readonly IDocumentRepository<Server, Guid> _witsmlServerRepository;
        private const string AuthorizationHeader = "Authorization";

        public CredentialsService(
            IDataProtectionProvider dataProtectionProvider,
            IHttpContextAccessor httpContextAccessor,
            IOptions<WitsmlClientCapabilities> clientCapabilities,
            IWitsmlSystemCredentials witsmlServerCredentials,
            IDocumentRepository<Server, Guid> witsmlServerRepository,
            ILogger<CredentialsService> logger)
        {
            _dataProtector = dataProtectionProvider.CreateProtector("WitsmlServerPassword").ToTimeLimitedDataProtector();
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentException("Missing IHttpContextAccessor");
            _logger = logger ?? throw new ArgumentException("Missing ILogger");
            _clientCapabilities = clientCapabilities.Value;
            _witsmlServerCredentials = witsmlServerCredentials;
            _witsmlServerRepository = witsmlServerRepository ?? throw new ArgumentException("Missing WitsmlServerRepository");
        }

        public async Task<string> Authorize(Uri serverUrl)
        {
            if (_httpContextAccessor.HttpContext == null) { return ""; }

            IHeaderDictionary headers = _httpContextAccessor.HttpContext.Request.Headers;
            string base64EncodedCredentials = headers[AuthorizationHeader].ToString()["Basic ".Length..].Trim();
            ServerCredentials credentials = new(serverUrl.ToString(), new BasicCredentials(base64EncodedCredentials));

            await VerifyCredentials(credentials);

            string protectedPayload = _dataProtector.Protect(credentials.Password, TimeSpan.FromDays(1));

            return protectedPayload;
        }

        public string Decrypt(ICredentials credentials)
        {
            return _dataProtector.Unprotect(credentials.Password);
        }

        public bool VerifyIsEncrypted(ICredentials credentials)
        {
            try
            {
                Decrypt(credentials);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task VerifyCredentials(ServerCredentials serverCreds)
        {
            WitsmlClient witsmlClient = new(serverCreds.Host, serverCreds.UserId, serverCreds.Password, _clientCapabilities);
            await witsmlClient.TestConnectionAsync();
        }

        public async Task<bool> AuthorizeWithToken(HttpRequest httpRequest)
        {
            try
            {
                IHeaderDictionary headers = httpRequest.Headers;
                List<ICredentials> credentialsList = await ExtractCredentialsFromHeader(headers);
                StringValues server = headers["Witsml-ServerUrl"];
                ServerCredentials serverCreds = new(server, credentialsList[0].UserId, Decrypt(credentialsList[0]));
                await VerifyCredentials(serverCreds);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed authorization with token: {message}", ex.Message);
                return false;
            }
            return true;
        }

        public Task<List<ICredentials>> ExtractCredentialsFromHeader(IHeaderDictionary headers)
        {
            Task<List<ICredentials>> credentials = Task.FromResult(new List<ICredentials>());
            string scheme = headers["Authorization"].ToString().Split()[0];
            if (string.IsNullOrEmpty(scheme)) { return credentials; }

            string base64Data = headers["Authorization"].ToString().Split()[1];
            string server = headers["Witsml-ServerUrl"].ToString();
            string sourceServer = headers["Witsml-Source-ServerUrl"].ToString();

            if (scheme == "Basic") { credentials = Task.FromResult(ParseBasicAuthorization(base64Data, sourceServer, server)); }
            else if (scheme == "Bearer") { credentials = ParseBearerAuthorization(base64Data, sourceServer, server); }

            return credentials;
        }

        private async Task<bool> UserHasRoleForHosts(string[] roles, string[] hosts)
        {
            bool result = true;
            IEnumerable<Server> allServers = await _witsmlServerRepository.GetDocumentsAsync();
            foreach (string host in hosts.Where(h => !string.IsNullOrEmpty(h)))
            {
                bool systemCredsExists = _witsmlServerCredentials.WitsmlCreds.Any(n => n.Host == host);
                IEnumerable<Server> hostServer = allServers.Where(n => n.Url.ToString() == host);
                bool validRole = hostServer.Any(n => roles.Contains(n.Role));
                result &= systemCredsExists & validRole;
            }
            return result;
        }

        private static List<ICredentials> ParseBasicAuthorization(string base64Data, string sourceServer, string server)
        {
            List<ICredentials> credentials = new();
            string credentialString = Encoding.UTF8.GetString(Convert.FromBase64String(base64Data));
            string[] usernamesAndPasswords = credentialString.Split(':');
            credentials.Add(new ServerCredentials(server, usernamesAndPasswords[0], usernamesAndPasswords[1]));
            if (usernamesAndPasswords.Length == 4 && !string.IsNullOrEmpty(sourceServer))
            {
                credentials.Add(new ServerCredentials(sourceServer, usernamesAndPasswords[2], usernamesAndPasswords[3]));
            }
            return credentials;
        }

        private async Task<List<ICredentials>> ParseBearerAuthorization(string base64Data, string sourceServer, string server)
        {
            List<ICredentials> credentials = new();
            JwtSecurityTokenHandler handler = new();
            JwtSecurityToken jwt = handler.ReadJwtToken(base64Data);
            string[] roles = jwt.Claims.Where(n => n.Type == "roles").Select(n => n.Value).ToArray();
            _logger.LogInformation("{roles}", string.Join(",", roles));
            if (await UserHasRoleForHosts(roles, new string[] { server, sourceServer }))
            {
                ServerCredentials creds = _witsmlServerCredentials.WitsmlCreds.Single(n => n.Host == server);
                credentials.Add(new ServerCredentials(server, creds.UserId, creds.Password));
            }

            return credentials;
        }

    }

}
