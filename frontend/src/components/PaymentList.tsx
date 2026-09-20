import type { Payment } from '../types';

interface Props {
  payments: Payment[];
  loading: boolean;
  confirmingId: string | null;
  onConfirm: (paymentId: string) => void;
}

const money = new Intl.NumberFormat('en-ZA', {
  style: 'currency',
  currency: 'ZAR',
});

function formatTimestamp(iso: string): string {
  // Timestamps arrive as UTC and are rendered in the viewer's local zone.
  return new Date(iso).toLocaleString();
}

export function PaymentList({ payments, loading, confirmingId, onConfirm }: Props) {
  if (loading && payments.length === 0) {
    return (
      <section className="card">
        <h2>Payments</h2>
        <p className="muted">Loading payments…</p>
      </section>
    );
  }

  if (payments.length === 0) {
    return (
      <section className="card">
        <h2>Payments</h2>
        <p className="muted">No payments yet. Create one above.</p>
      </section>
    );
  }

  return (
    <section className="card">
      <h2>
        Payments <span className="muted">({payments.length})</span>
      </h2>

      <table>
        <thead>
          <tr>
            <th>Customer</th>
            <th className="numeric">Amount</th>
            <th>Status</th>
            <th>Created</th>
            <th aria-label="Actions" />
          </tr>
        </thead>
        <tbody>
          {payments.map((payment) => {
            const isConfirming = confirmingId === payment.id;

            return (
              <tr key={payment.id}>
                <td>
                  {payment.customerId}
                  <br />
                  <span className="muted mono">{payment.id}</span>
                </td>
                <td className="numeric">{money.format(payment.amount)}</td>
                <td>
                  <span className={`badge badge-${payment.status.toLowerCase()}`}>
                    {payment.status}
                  </span>
                </td>
                <td>
                  {formatTimestamp(payment.createdAt)}
                  {payment.confirmedAt && (
                    <>
                      <br />
                      <span className="muted">
                        confirmed {formatTimestamp(payment.confirmedAt)}
                      </span>
                    </>
                  )}
                </td>
                <td>
                  {payment.status === 'Pending' && (
                    <button
                      type="button"
                      onClick={() => onConfirm(payment.id)}
                      disabled={isConfirming}
                    >
                      {isConfirming ? 'Confirming…' : 'Confirm'}
                    </button>
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </section>
  );
}
