import type { CreatePaymentRequest, Payment, ProblemDetails } from './types';

const baseUrl = (import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5057').replace(/\/$/, '');

/** An error carrying a message already fit to show a user. */
export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

/**
 * Turns a failed response into a readable message. The API speaks ProblemDetails,
 * including validation failures, so this is the one place that has to understand
 * that shape.
 */
async function toError(response: Response): Promise<ApiError> {
  let message = `Request failed with status ${response.status}.`;

  try {
    const problem = (await response.json()) as ProblemDetails;

    if (problem.errors) {
      const fieldErrors = Object.values(problem.errors).flat();
      if (fieldErrors.length > 0) {
        message = fieldErrors.join(' ');
        return new ApiError(message, response.status);
      }
    }

    message = problem.detail ?? problem.title ?? message;
  } catch {
    // Not JSON, or an empty body. The status-based message above will do.
  }

  return new ApiError(message, response.status);
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;

  try {
    response = await fetch(`${baseUrl}${path}`, {
      headers: { 'Content-Type': 'application/json' },
      ...init,
    });
  } catch {
    // fetch only rejects when the request never got an answer: the API is down,
    // the port is wrong, or CORS blocked it. Worth saying so plainly rather than
    // reporting "failed to fetch".
    throw new ApiError(`Cannot reach the API at ${baseUrl}. Is the backend running?`, 0);
  }

  if (!response.ok) {
    throw await toError(response);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export const api = {
  listPayments: () => request<Payment[]>('/payments'),

  createPayment: (payment: CreatePaymentRequest, idempotencyKey: string) =>
    request<Payment>('/payments', {
      method: 'POST',
      // Sent so that a retry of this same submission cannot create a second
      // payment. The server keys off it; see the README.
      headers: {
        'Content-Type': 'application/json',
        'Idempotency-Key': idempotencyKey,
      },
      body: JSON.stringify(payment),
    }),

  confirmPayment: (paymentId: string) =>
    request<Payment>(`/simulate-confirmation/${paymentId}`, { method: 'POST' }),
};
