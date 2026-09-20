import { useCallback, useEffect, useState } from 'react';
import { ApiError, api } from './api';
import { Notification, type NotificationState } from './components/Notification';
import { PaymentForm } from './components/PaymentForm';
import { PaymentList } from './components/PaymentList';
import type { CreatePaymentRequest, Payment } from './types';

function messageFor(error: unknown): string {
  return error instanceof ApiError || error instanceof Error
    ? error.message
    : 'Something went wrong.';
}

export default function App() {
  const [payments, setPayments] = useState<Payment[]>([]);
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);
  const [confirmingId, setConfirmingId] = useState<string | null>(null);
  const [notification, setNotification] = useState<NotificationState | null>(null);

  const notify = useCallback((kind: NotificationState['kind'], message: string) => {
    setNotification({ kind, message, key: Date.now() });
  }, []);

  const dismiss = useCallback(() => setNotification(null), []);

  const refresh = useCallback(async () => {
    setLoading(true);
    try {
      setPayments(await api.listPayments());
    } catch (error) {
      notify('error', messageFor(error));
    } finally {
      setLoading(false);
    }
  }, [notify]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  async function handleCreate(request: CreatePaymentRequest) {
    setCreating(true);
    try {
      // A key generated per submission, so a retry of this submission is
      // recognised by the server as the same one rather than a new payment.
      const idempotencyKey = crypto.randomUUID();

      const created = await api.createPayment(request, idempotencyKey);
      notify('success', `Payment created for ${created.customerId}.`);
      await refresh();
    } catch (error) {
      notify('error', messageFor(error));
    } finally {
      setCreating(false);
    }
  }

  async function handleConfirm(paymentId: string) {
    setConfirmingId(paymentId);
    try {
      await api.confirmPayment(paymentId);
      notify('success', 'Confirmation event applied.');
      await refresh();
    } catch (error) {
      notify('error', messageFor(error));
    } finally {
      setConfirmingId(null);
    }
  }

  return (
    <main>
      <header>
        <h1>PaymentOrchestrator Lite</h1>
        <p className="muted">
          Create a payment, then apply a simulated upstream confirmation event to it.
        </p>
      </header>

      <Notification notification={notification} onDismiss={dismiss} />

      <PaymentForm onSubmit={handleCreate} busy={creating} />

      <PaymentList
        payments={payments}
        loading={loading}
        confirmingId={confirmingId}
        onConfirm={handleConfirm}
      />

      <footer className="muted">
        <button type="button" onClick={() => void refresh()} disabled={loading}>
          {loading ? 'Refreshing…' : 'Refresh'}
        </button>
      </footer>
    </main>
  );
}
