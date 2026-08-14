namespace TripPlanner.Application.Common.Exceptions;

/// <summary>
/// Thrown by an Infrastructure adapter when an external service it fronts could not
/// be reached or did not answer usably — a connection failure, a timeout, an error
/// status, or a response body that does not parse.
///
/// It exists so the Application layer can hold a fallback policy for "that source
/// didn't come through" without knowing which technology the adapter speaks. A
/// service catching this does not care whether <c>IEmailSender</c> is SMTP or an
/// HTTP API, so swapping either one cannot silently break the fallback — which is
/// exactly what catching <c>SmtpException</c>/<c>HttpRequestException</c> up here
/// used to risk.
///
/// Deliberately NOT mapped by ExceptionHandlingMiddleware. A service with a
/// fallback (serve a stale cache entry; save the destination without a photo)
/// catches it; anywhere else it reaches the generic 500 branch, which is what an
/// unusable external dependency with no fallback actually is.
///
/// A caller-driven cancellation is never reported as this: adapters translate only
/// when their own CancellationToken was not the cause, so a cancellation still
/// propagates as <see cref="TaskCanceledException"/> and is never mistaken for an
/// outage.
/// </summary>
public class ExternalServiceUnavailableException : Exception
{
    public ExternalServiceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
