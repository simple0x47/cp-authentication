using System.Net;
using Core;
using Core.Config;
using Core.Secrets;
using Cuplan.Authentication.Models;
using Cuplan.Authentication.ServiceModels;
using LoginPayload = Cuplan.Authentication.Models.LoginPayload;
using RefreshTokenPayload = Cuplan.Authentication.ServiceModels.RefreshTokenPayload;
using SignUpPayload = Cuplan.Authentication.Models.SignUpPayload;

namespace Cuplan.Authentication.Services;

public class Auth0Provider : IAuthProvider
{
    private const string ResourceOwnerGrant = "password";
    private const string RefreshTokenGrant = "refresh_token";
    private const string RequiredScopes = "openid offline_access";
    private const string SignUpEndpoint = "dbconnections/signup";
    private const string LoginEndpoint = "oauth/token";
    private const string RefreshTokenEndpoint = "oauth/token";
    private const string ChangePasswordEndpoint = "dbconnections/change_password";
    private const string IdPrefix = "auth0|";

    private readonly HttpClient _client;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _database;
    private readonly TimeSpan _forgotPasswordTimeout;
    private readonly string _identityProviderAudience;
    private readonly string _identityProviderUrl;

    private readonly ILogger<Auth0Provider> _logger;
    private readonly TimeSpan _loginTimeout;
    private readonly TimeSpan _refreshTokenTimeout;

    private readonly TimeSpan _signUpTimeout;

    public Auth0Provider(IConfigProvider configProvider, ISecretsManager secretsManager, ILogger<Auth0Provider> logger)
    {
        // Expects the identity provider url to finish with '/'.
        _identityProviderUrl =
            configProvider.Get<string>("application.yaml|IdentityProvider:Authority").Result.Unwrap();
        _identityProviderAudience =
            configProvider.Get<string>("application.yaml|IdentityProvider:Audience").Result.Unwrap();

        if (_identityProviderUrl is null) throw new NullReferenceException("'_identityProviderUrl' is null.");

        _clientId = secretsManager.Get(configProvider.Get<string>("application.yaml|Auth0:ClientIdSecret").Result
            .Unwrap());

        if (_clientId is null) throw new NullReferenceException("'_clientId' is null.");

        _clientSecret =
            secretsManager.Get(configProvider.Get<string>("application.yaml|Auth0:ClientSecretSecret").Result.Unwrap());

        if (_clientSecret is null) throw new NullReferenceException("'_clientSecret' is null.");

        _database = configProvider.Get<string>("application.yaml|Auth0:Database").Result.Unwrap();

        if (_database is null) throw new NullReferenceException("'_database' is null.");

        _client = new HttpClient();

        _signUpTimeout =
            TimeSpan.FromSeconds(configProvider.Get<double>("application.yaml|AuthProvider:SignUpTimeout").Result
                .Unwrap());
        _loginTimeout =
            TimeSpan.FromSeconds(configProvider.Get<double>("application.yaml|AuthProvider:LoginTimeout").Result
                .Unwrap());
        _forgotPasswordTimeout =
            TimeSpan.FromSeconds(configProvider.Get<double>("application.yaml|AuthProvider:ForgotPasswordTimeout")
                .Result.Unwrap());
        _refreshTokenTimeout =
            TimeSpan.FromSeconds(configProvider.Get<double>("application.yaml|AuthProvider:RefreshTokenTimeout").Result
                .Unwrap());

        _logger = logger;
    }

    public async Task<Result<string, Error<string>>> SignUp(SignUpPayload signUp)
    {
        try
        {
            ServiceModels.SignUpPayload payload = new();
            payload.client_id = _clientId;
            payload.email = signUp.Email;
            payload.password = signUp.Password;
            payload.connection = _database;

            HttpResponseMessage response =
                await _client.PostAsync($"{_identityProviderUrl}{SignUpEndpoint}", JsonContent.Create(payload))
                    .WaitAsync(_signUpTimeout);

            if (response.StatusCode != HttpStatusCode.OK)
                return await HandleRegisterAuth0ErrorResponse(response);

            SignUpOkResponse okResponse = await response.Content.ReadFromJsonAsync<SignUpOkResponse>();

            if (okResponse._id is null)
                return Result<string, Error<string>>.Err(new Error<string>(ErrorKind.OkResponseNull,
                    "deserialized ok response '_id' is null"));

            // the prefix is added so the ids represented within the tokens are equal to the ones used
            // within Cuplan.
            return Result<string, Error<string>>.Ok($"{IdPrefix}{okResponse._id}");
        }
        catch (TimeoutException)
        {
            return Result<string, Error<string>>.Err(new Error<string>(ErrorKind.TimedOut, "sign up timed out"));
        }
        catch (Exception e)
        {
            string message = $"failed to sign up: {e}";
            _logger.LogInformation(message);
            return Result<string, Error<string>>.Err(new Error<string>(ErrorKind.ServiceError, message));
        }
    }

    public async Task<Result<LoginSuccessPayload, Error<string>>> Login(LoginPayload login)
    {
        try
        {
            ServiceModels.LoginPayload payload = new()
            {
                audience = _identityProviderAudience,
                client_id = _clientId,
                client_secret = _clientSecret,
                grant_type = ResourceOwnerGrant,
                username = login.Email,
                password = login.Password,
                scope = RequiredScopes
            };

            HttpResponseMessage response =
                await _client.PostAsync($"{_identityProviderUrl}{LoginEndpoint}", JsonContent.Create(payload))
                    .WaitAsync(_loginTimeout);

            if (response.StatusCode != HttpStatusCode.OK)
                return await HandleLoginAuth0ErrorResponse(response);

            LoginOkResponse okResponse = await response.Content.ReadFromJsonAsync<LoginOkResponse>();

            if (okResponse.access_token is null)
                return Result<LoginSuccessPayload, Error<string>>.Err(new Error<string>(ErrorKind.OkResponseNull,
                    "deserialized ok response's content is null"));

            LoginSuccessPayload successPayload = okResponse;

            return Result<LoginSuccessPayload, Error<string>>.Ok(successPayload);
        }
        catch (TimeoutException)
        {
            return Result<LoginSuccessPayload, Error<string>>.Err(new Error<string>(ErrorKind.TimedOut,
                "login timed out"));
        }
        catch (Exception e)
        {
            string message = $"failed to login: {e}";
            _logger.LogInformation(message);
            return Result<LoginSuccessPayload, Error<string>>.Err(new Error<string>(ErrorKind.ServiceError, message));
        }
    }

    public async Task<Result<Empty, Error<string>>> ForgotPassword(ForgotPasswordPayload forgotPassword)
    {
        try
        {
            ChangePasswordPayload payload = new()
            {
                client_id = _clientId,
                connection = _database,
                email = forgotPassword.Email
            };

            HttpResponseMessage response = await _client.PostAsync($"{_identityProviderUrl}{ChangePasswordEndpoint}",
                JsonContent.Create(payload)).WaitAsync(_forgotPasswordTimeout);

            if (response.StatusCode != HttpStatusCode.OK)
                return Result<Empty, Error<string>>.Err(new Error<string>(ErrorKind.UnknownError,
                    "Auth0 replied with a non-ok response"));

            return Result<Empty, Error<string>>.Ok(new Empty());
        }
        catch (TimeoutException)
        {
            return Result<Empty, Error<string>>.Err(new Error<string>(ErrorKind.TimedOut,
                "forgot password timed out"));
        }
        catch (Exception e)
        {
            string message = $"forgot password failed: {e}";
            _logger.LogInformation(message);
            return Result<Empty, Error<string>>.Err(new Error<string>(ErrorKind.ServiceError, message));
        }
    }

    public async Task<Result<LoginSuccessPayload, Error<string>>> RefreshToken(string refreshToken)
    {
        try
        {
            RefreshTokenPayload payload = new()
            {
                grant_type = RefreshTokenGrant,
                client_id = _clientId,
                client_secret = _clientSecret,
                refresh_token = refreshToken
            };

            HttpResponseMessage response = await _client.PostAsync($"{_identityProviderUrl}{RefreshTokenEndpoint}",
                JsonContent.Create(payload));

            if (response.StatusCode != HttpStatusCode.OK)
            {
                LoginErrorResponse error = await response.Content.ReadFromJsonAsync<LoginErrorResponse>();
                return Result<LoginSuccessPayload, Error<string>>.Err(new Error<string>(error.error,
                    error.error_description));
            }

            LoginOkResponse successPayload = await response.Content.ReadFromJsonAsync<LoginOkResponse>();
            return Result<LoginSuccessPayload, Error<string>>.Ok(successPayload);
        }
        catch (TimeoutException)
        {
            return Result<LoginSuccessPayload, Error<string>>.Err(new Error<string>(ErrorKind.TimedOut,
                "refresh token timed out"));
        }
        catch (Exception e)
        {
            string message = $"refresh token failed: {e}";
            _logger.LogInformation(message);
            return Result<LoginSuccessPayload, Error<string>>.Err(new Error<string>(ErrorKind.ServiceError, message));
        }
    }

    private async Task<Result<string, Error<string>>> HandleRegisterAuth0ErrorResponse(HttpResponseMessage response)
    {
        RegisterErrorResponse errorResponse =
            await response.Content.ReadFromJsonAsync<RegisterErrorResponse>();

        if (errorResponse.code is null)
            return Result<string, Error<string>>.Err(new Error<string>(ErrorKind.ErrorResponseNull,
                "deserialized 'code' is null"));

        return Result<string, Error<string>>.Err(new Error<string>(errorResponse.code, errorResponse.description));
    }

    private async Task<Result<LoginSuccessPayload, Error<string>>> HandleLoginAuth0ErrorResponse(
        HttpResponseMessage response)
    {
        LoginErrorResponse errorResponse = await response.Content.ReadFromJsonAsync<LoginErrorResponse>();

        if (errorResponse.error is null)
            return Result<LoginSuccessPayload, Error<string>>.Err(new Error<string>(ErrorKind.ErrorResponseNull,
                "deserialized 'error' is null"));

        return Result<LoginSuccessPayload, Error<string>>.Err(new Error<string>(errorResponse.error,
            errorResponse.error_description));
    }
}