import { ApiError, api } from '../api';

function respondWith(body: unknown, status: number) {
  return vi.fn().mockResolvedValue(
    new Response(JSON.stringify(body), {
      status,
      headers: { 'Content-Type': 'application/json' },
    }),
  );
}

describe('api', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('returns the parsed payment list on success', async () => {
    vi.stubGlobal('fetch', respondWith([{ id: 'abc' }], 200));

    const payments = await api.listPayments();

    expect(payments).toEqual([{ id: 'abc' }]);
  });

  it('turns ProblemDetails validation errors into one readable message', async () => {
    vi.stubGlobal(
      'fetch',
      respondWith({ errors: { Amount: ['Amount must be between 0.01 and 1000000.00.'] } }, 400),
    );

    const error = (await api.listPayments().catch((e: unknown) => e)) as ApiError;

    expect(error).toBeInstanceOf(ApiError);
    expect(error.message).toContain('Amount must be between');
  });

  it('falls back to the ProblemDetails detail when there are no field errors', async () => {
    vi.stubGlobal('fetch', respondWith({ detail: 'No payment exists with id 0.' }, 404));

    const error = (await api.listPayments().catch((e: unknown) => e)) as ApiError;

    expect(error.message).toBe('No payment exists with id 0.');
    expect(error.status).toBe(404);
  });

  it('reports an unreachable API rather than a raw fetch failure', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')));

    const error = (await api.listPayments().catch((e: unknown) => e)) as ApiError;

    expect(error.message).toMatch(/cannot reach the api/i);
    expect(error.status).toBe(0);
  });

  it('sends the idempotency key when creating a payment', async () => {
    const fetchMock = respondWith({ id: 'abc' }, 201);
    vi.stubGlobal('fetch', fetchMock);

    await api.createPayment({ customerId: 'CUST-001', amount: 10 }, 'key-abc');

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBe('key-abc');
  });
});
