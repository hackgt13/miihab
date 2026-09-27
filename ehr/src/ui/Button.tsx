import { type ButtonHTMLAttributes } from 'react'

type Variant = 'primary' | 'outline' | 'ghost' | 'accent'
type Size = 'sm' | 'md' | 'lg'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  size?: Size
  fullWidth?: boolean
}

const variantStyles: Record<Variant, string> = {
  primary: 'bg-[#6FB8C4] text-[#232B2F] font-semibold hover:bg-[#86C5CF] active:bg-[#5AA8B4]',
  outline: 'border border-[#3D484E] text-[#E4E9EB] hover:border-[#6FB8C4] hover:text-[#6FB8C4]',
  ghost:   'text-[#A3B0B6] hover:text-[#E4E9EB] hover:bg-[#323C41]',
  accent:  'bg-[#9CC2B5] text-[#232B2F] font-semibold hover:bg-[#6FB8C4]',
}

const sizeStyles: Record<Size, string> = {
  sm: 'px-3 py-1.5 text-xs',
  md: 'px-5 py-2.5 text-sm',
  lg: 'px-7 py-3 text-sm',
}

export function Button({
  variant = 'primary',
  size = 'md',
  fullWidth = false,
  className = '',
  children,
  ...props
}: ButtonProps) {
  return (
    <button
      className={[
        'inline-flex items-center justify-center tracking-wide',
        'rounded-sm transition-colors duration-150 cursor-pointer',
        'disabled:opacity-40 disabled:cursor-not-allowed',
        variantStyles[variant],
        sizeStyles[size],
        fullWidth ? 'w-full' : '',
        className,
      ]
        .filter(Boolean)
        .join(' ')}
      {...props}
    >
      {children}
    </button>
  )
}
