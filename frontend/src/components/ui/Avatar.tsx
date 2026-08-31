import { Handshake } from 'lucide-react'

export function LogoMark({ size = 'md' }: { size?: 'md' | 'lg' }) {
  return (
    <span className="logo-mark" aria-hidden="true">
      <Handshake size={size === 'lg' ? 24 : 18} />
    </span>
  )
}

export function Avatar({
  name,
  size = 'md',
}: {
  name: string
  size?: 'sm' | 'md' | 'lg'
}) {
  const initials = name
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('')
  return (
    <span
      className={`avatar avatar--${size}`}
      aria-hidden="true"
      title={name}
    >
      {initials || '?'}
    </span>
  )
}