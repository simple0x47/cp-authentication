using Core;
using Cuplan.Authentication.Models;

namespace Cuplan.Authentication.Services;

public interface IAuthProvider
{
    /// <summary>
    ///     Signs up an user for the specified payload.
    /// </summary>
    /// <param name="signUp"></param>
    /// <returns>The user id or an error.</returns>
    public Task<Result<string, Error<string>>> SignUp(SignUpPayload signUp);

    /// <summary>
    ///     Logs in an user for the specified payload.
    /// </summary>
    /// <param name="login"></param>
    /// <returns>A <see cref="LoginSuccessPayload" /> or an error.</returns>
    public Task<Result<LoginSuccessPayload, Error<string>>> Login(LoginPayload login);

    /// <summary>
    ///     Begins the process of creating a new password.
    /// </summary>
    /// <param name="forgotPassword"></param>
    /// <returns><see cref="Empty" /> or an error.</returns>
    public Task<Result<Empty, Error<string>>> ForgotPassword(ForgotPasswordPayload forgotPassword);

    /// <summary>
    ///     Uses a refresh token to obtain a new, valid, access token after the expiration of the previous one.
    /// </summary>
    /// <param name="refreshToken"></param>
    /// <returns>The new set of tokens packed within a <see cref="LoginSuccessPayload" /> or an error.</returns>
    public Task<Result<LoginSuccessPayload, Error<string>>> RefreshToken(string refreshToken);
}