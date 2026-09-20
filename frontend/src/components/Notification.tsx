import { useEffect } from 'react';

export type NotificationKind = 'success' | 'error';

export interface NotificationState {
  kind: NotificationKind;
  message: string;
  /** Changes on every notification so repeats of the same message still re-show. */
  key: number;
}

interface Props {
  notification: NotificationState | null;
  onDismiss: () => void;
}

export function Notification({ notification, onDismiss }: Props) {
  useEffect(() => {
    if (!notification) {
      return;
    }

    // Errors stay until dismissed; successes clear themselves.
    if (notification.kind === 'error') {
      return;
    }

    const timer = window.setTimeout(onDismiss, 4000);
    return () => window.clearTimeout(timer);
  }, [notification, onDismiss]);

  if (!notification) {
    return null;
  }

  return (
    <div
      className={`notification notification-${notification.kind}`}
      role={notification.kind === 'error' ? 'alert' : 'status'}
    >
      <span>{notification.message}</span>
      <button type="button" className="dismiss" onClick={onDismiss} aria-label="Dismiss">
        ×
      </button>
    </div>
  );
}
