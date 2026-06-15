import type { CSSProperties } from 'react'

interface SpinnerProps {
  size?: number
  style?: CSSProperties
}

export function Spinner({ size = 20, style }: SpinnerProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      style={{ animation: 'spin 0.8s linear infinite', ...style }}
      fill="none"
    >
      <circle
        cx="12"
        cy="12"
        r="10"
        stroke="var(--spinner-track)"
        strokeWidth="3"
      />
      <path
        d="M12 2a10 10 0 0 1 10 10"
        stroke="var(--spinner-accent)"
        strokeWidth="3"
        strokeLinecap="round"
      />
    </svg>
  )
}
