import type { ReactNode } from 'react'

type ModalProps = {
  children: ReactNode
  onClose?: () => void
  /** Optional: max width class (e.g. max-w-md, max-w-sm) */
  maxWidth?: 'sm' | 'md' | 'lg'
}

const maxWidthClass = {
  sm: 'max-w-sm',
  md: 'max-w-md',
  lg: 'max-w-lg',
} as const

export function Modal({ children, onClose, maxWidth = 'md' }: ModalProps) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4">
      <div
        className={`w-full rounded-xl border border-neutral-800 bg-neutral-900 p-6 shadow-xl ${maxWidthClass[maxWidth]}`}
        role="dialog"
        aria-modal="true"
      >
        {children}
        {onClose && (
          <button
            type="button"
            onClick={onClose}
            className="sr-only focus:not-sr-only focus:absolute focus:top-4 focus:right-4 focus:rounded focus:bg-neutral-800 focus:px-2 focus:py-1 focus:text-neutral-300"
          >
            Close
          </button>
        )}
      </div>
    </div>
  )
}
