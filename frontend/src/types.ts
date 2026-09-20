export type PaymentStatus = 'Pending' | 'Confirmed';

export interface Payment {
  id: string;
  customerId: string;
  amount: number;
  status: PaymentStatus;
  createdAt: string;
  confirmedAt: string | null;
}

export interface CreatePaymentRequest {
  customerId: string;
  amount: number;
}

/** RFC 9457 ProblemDetails, which is what the API returns for every error. */
export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}
