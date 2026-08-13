import {
  AxiosError,
  AxiosHeaders,
  type AxiosInstance,
  type AxiosResponse,
  type InternalAxiosRequestConfig,
} from 'axios';

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

/** One outgoing request as it would have gone on the wire. */
export interface RecordedRequest {
  method: string;
  url: string;
  params?: Record<string, unknown>;
  /** Parsed back from the serialized request body; undefined for GET/DELETE. */
  body?: unknown;
  headers: AxiosHeaders;
}

/**
 * Swaps `instance`'s adapter for one that records every request and answers with
 * `data` (or fails with `status` when it is >= 400), and returns the recording
 * plus a `restore()` to put the real adapter back.
 *
 * The adapter is the seam *below* the interceptors, so a recorded request has
 * already been through them and through baseURL/param serialization — which is
 * what makes this able to catch a wrong path or a missing Authorization header.
 * Spying on `apiClient.get` instead would sit above all of that and see only
 * what the wrapper passed in.
 */
export function recordRequests(
  instance: AxiosInstance,
  { data = {}, status = 200 }: { data?: unknown; status?: number } = {},
): { requests: RecordedRequest[]; restore: () => void } {
  const original = instance.defaults.adapter;
  const requests: RecordedRequest[] = [];

  instance.defaults.adapter = async (config: InternalAxiosRequestConfig) => {
    const headers = config.headers as AxiosHeaders;
    requests.push({
      method: (config.method ?? 'get').toLowerCase(),
      url: config.url ?? '',
      params: config.params,
      body: typeof config.data === 'string' ? JSON.parse(config.data) : config.data,
      headers,
    });

    const response = {
      data,
      status,
      statusText: '',
      headers: new AxiosHeaders(),
      config,
    } as AxiosResponse;

    if (status >= 400) {
      throw new AxiosError('Request failed', 'ERR_BAD_REQUEST', config, undefined, response);
    }
    return response;
  };

  return { requests, restore: () => { instance.defaults.adapter = original; } };
}
