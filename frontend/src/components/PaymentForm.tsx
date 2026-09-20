import { useState, type FormEvent } from 'react';
import type { CreatePaymentRequest } from '../types';

interface Props {
  onSubmit: (payment: CreatePaymentRequest) => Promise<void>;
  busy: boolean;
}

interface Errors {
  customerId?: string;
  amount?: string;
}

/**
 * Client-side validation mirrors the server's rules so the common mistakes are
 * caught without a round trip. It does not replace them: the server validates
 * independently, because anything can call the API.
 */
function validate(customerId: string, amount: string): Errors {
  const errors: Errors = {};

  if (customerId.trim().length === 0) {
    errors.customerId = 'Customer ID is required.';
  } else if (customerId.trim().length > 100) {
    errors.customerId = 'Customer ID must be 100 characters or fewer.';
  }

  const parsed = Number(amount);

  if (amount.trim().length === 0) {
    errors.amount = 'Amount is required.';
  } else if (!Number.isFinite(parsed)) {
    errors.amount = 'Amount must be a number.';
  } else if (parsed < 0.01) {
    errors.amount = 'Amount must be at least 0.01.';
  } else if (parsed > 1_000_000) {
    errors.amount = 'Amount must be 1,000,000.00 or less.';
  } else if (Math.round(parsed * 100) !== parsed * 100) {
    errors.amount = 'Amount cannot have more than two decimal places.';
  }

  return errors;
}

export function PaymentForm({ onSubmit, busy }: Props) {
  const [customerId, setCustomerId] = useState('');
  const [amount, setAmount] = useState('');
  const [errors, setErrors] = useState<Errors>({});

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const found = validate(customerId, amount);
    setErrors(found);

    if (Object.keys(found).length > 0) {
      return;
    }

    await onSubmit({ customerId: customerId.trim(), amount: Number(amount) });

    setCustomerId('');
    setAmount('');
    setErrors({});
  }

  return (
    <form className="card" onSubmit={handleSubmit} noValidate>
      <h2>New payment</h2>

      <div className="field">
        <label htmlFor="customerId">Customer ID</label>
        <input
          id="customerId"
          value={customerId}
          onChange={(e) => setCustomerId(e.target.value)}
          placeholder="CUST-001"
          disabled={busy}
          aria-invalid={Boolean(errors.customerId)}
          aria-describedby={errors.customerId ? 'customerId-error' : undefined}
        />
        {errors.customerId && (
          <p className="field-error" id="customerId-error">
            {errors.customerId}
          </p>
        )}
      </div>

      <div className="field">
        <label htmlFor="amount">Amount</label>
        <input
          id="amount"
          type="number"
          step="0.01"
          min="0.01"
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
          placeholder="1499.99"
          disabled={busy}
          aria-invalid={Boolean(errors.amount)}
          aria-describedby={errors.amount ? 'amount-error' : undefined}
        />
        {errors.amount && (
          <p className="field-error" id="amount-error">
            {errors.amount}
          </p>
        )}
      </div>

      {/* Disabled while in flight: the everyday cause of duplicate payments is a
          second click before the first response lands. */}
      <button type="submit" className="primary" disabled={busy}>
        {busy ? 'Creating…' : 'Create payment'}
      </button>
    </form>
  );
}
