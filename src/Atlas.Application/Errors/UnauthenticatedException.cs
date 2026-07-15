namespace Atlas.Application.Errors;

/// <summary>No valid session — the Api maps this to 401.</summary>
public sealed class UnauthenticatedException(string message = "Authentication required.")
    : Exception(message);
