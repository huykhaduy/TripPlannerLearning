import { AxiosError, AxiosHeaders } from 'axios';

/**
 * Builds the kinds of rejection the axios instance in `api/client.ts` actually
 * produces, so tests exercise the real `getErrorMessage`/`getErrorStatus` branches
 * instead of hand-rolled objects that merely look close enough.
 */

/** A failed request that DID get an HTTP response, optionally with a ProblemDetails body. */
export function httpError(status: number, data?: unknown): AxiosError {
  const config = { headers: new AxiosHeaders() };
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', config, undefined, {
    status,
    statusText: '',
    data,
    headers: new AxiosHeaders(),
    config,
  });
}

/** No response at all — DNS failure, connection refused, timeout. */
export function networkError(): AxiosError {
  return new AxiosError('Network Error', 'ERR_NETWORK');
}
