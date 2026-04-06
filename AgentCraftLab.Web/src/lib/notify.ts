import { toast } from 'sonner'

interface NotifyOptions {
  description?: string
  action?: { label: string; onClick: () => void }
  id?: string
}

export const notify = {
  success: (title: string, opts?: NotifyOptions) =>
    toast.success(title, { description: opts?.description, duration: 4000 }),

  info: (title: string, opts?: NotifyOptions) =>
    toast.info(title, { description: opts?.description, duration: 4000 }),

  warning: (title: string, opts?: NotifyOptions) =>
    toast.warning(title, { description: opts?.description, duration: 6000 }),

  error: (title: string, opts?: NotifyOptions) =>
    toast.error(title, {
      description: opts?.description,
      duration: Infinity,
      action: opts?.action ? { label: opts.action.label, onClick: opts.action.onClick } : undefined,
      id: opts?.id,
    }),
}
