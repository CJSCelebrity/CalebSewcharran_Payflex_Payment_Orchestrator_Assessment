import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PaymentList } from '../components/PaymentList';
import type { Payment } from '../types';

const pending: Payment = {
  id: '0f8b5d4a-1c2e-4b6f-9a3d-7e8c9b0a1d2f',
  customerId: 'CUST-001',
  amount: 1499.99,
  status: 'Pending',
  createdAt: '2026-09-20T10:30:00Z',
  confirmedAt: null,
};

const confirmed: Payment = {
  ...pending,
  id: '1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d',
  customerId: 'CUST-002',
  status: 'Confirmed',
  confirmedAt: '2026-09-20T10:33:00Z',
};

describe('PaymentList', () => {
  it('shows a loading message before the first payments arrive', () => {
    render(<PaymentList payments={[]} loading confirmingId={null} onConfirm={vi.fn()} />);

    expect(screen.getByText(/loading payments/i)).toBeInTheDocument();
  });

  it('shows an empty state when there are no payments', () => {
    render(<PaymentList payments={[]} loading={false} confirmingId={null} onConfirm={vi.fn()} />);

    expect(screen.getByText(/no payments yet/i)).toBeInTheDocument();
  });

  it('renders a row per payment with its status', () => {
    render(
      <PaymentList payments={[pending, confirmed]} loading={false} confirmingId={null} onConfirm={vi.fn()} />,
    );

    expect(screen.getByText('CUST-001')).toBeInTheDocument();
    expect(screen.getByText('CUST-002')).toBeInTheDocument();
    expect(screen.getByText('Pending')).toBeInTheDocument();
    expect(screen.getByText('Confirmed')).toBeInTheDocument();
  });

  it('offers Confirm only for pending payments', () => {
    render(
      <PaymentList payments={[pending, confirmed]} loading={false} confirmingId={null} onConfirm={vi.fn()} />,
    );

    expect(screen.getAllByRole('button', { name: /^confirm$/i })).toHaveLength(1);
  });

  it('passes the payment id to onConfirm', async () => {
    const onConfirm = vi.fn();
    const user = userEvent.setup();
    render(<PaymentList payments={[pending]} loading={false} confirmingId={null} onConfirm={onConfirm} />);

    await user.click(screen.getByRole('button', { name: /^confirm$/i }));

    expect(onConfirm).toHaveBeenCalledWith(pending.id);
  });
});
