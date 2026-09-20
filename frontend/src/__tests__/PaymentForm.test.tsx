import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PaymentForm } from '../components/PaymentForm';

function renderForm(busy = false) {
  const onSubmit = vi.fn().mockResolvedValue(undefined);
  render(<PaymentForm onSubmit={onSubmit} busy={busy} />);
  return { onSubmit, user: userEvent.setup() };
}

describe('PaymentForm', () => {
  it('refuses to submit without a customer id', async () => {
    const { onSubmit, user } = renderForm();

    await user.type(screen.getByLabelText(/amount/i), '100');
    await user.click(screen.getByRole('button', { name: /create payment/i }));

    expect(await screen.findByText(/customer id is required/i)).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('refuses an amount below the minimum', async () => {
    const { onSubmit, user } = renderForm();

    await user.type(screen.getByLabelText(/customer id/i), 'CUST-001');
    await user.type(screen.getByLabelText(/amount/i), '0');
    await user.click(screen.getByRole('button', { name: /create payment/i }));

    expect(await screen.findByText(/at least 0\.01/i)).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('refuses more than two decimal places', async () => {
    const { onSubmit, user } = renderForm();

    await user.type(screen.getByLabelText(/customer id/i), 'CUST-001');
    await user.type(screen.getByLabelText(/amount/i), '10.999');
    await user.click(screen.getByRole('button', { name: /create payment/i }));

    expect(await screen.findByText(/two decimal places/i)).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('submits trimmed values and clears the form', async () => {
    const { onSubmit, user } = renderForm();

    await user.type(screen.getByLabelText(/customer id/i), '  CUST-001  ');
    await user.type(screen.getByLabelText(/amount/i), '1499.99');
    await user.click(screen.getByRole('button', { name: /create payment/i }));

    expect(onSubmit).toHaveBeenCalledWith({ customerId: 'CUST-001', amount: 1499.99 });
    await waitFor(() => expect(screen.getByLabelText(/customer id/i)).toHaveValue(''));
  });

  it('disables the submit button while a request is in flight', () => {
    renderForm(true);

    expect(screen.getByRole('button', { name: /creating/i })).toBeDisabled();
  });
});
