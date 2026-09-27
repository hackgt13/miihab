import { type InputHTMLAttributes, forwardRef } from 'react'

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string
  error?: string
}

export const Input = forwardRef<HTMLInputElement, InputProps>(
  ({ label, error, className = '', ...props }, ref) => {
    return (
      <div className="flex flex-col gap-1.5 w-full">
        {label && (
          <label className="text-[10px] font-medium text-[#A3B0B6] tracking-[0.15em] uppercase">
            {label}
          </label>
        )}
        <input
          ref={ref}
          className={[
            'w-full bg-[#2A3337] border text-[#E4E9EB] placeholder-[#A3B0B6]',
            'rounded-sm px-4 py-3 text-sm',
            'focus:outline-none transition-colors duration-150',
            error
              ? 'border-[#E08585] focus:border-[#E08585]'
              : 'border-[#3D484E] focus:border-[#6FB8C4]',
            className,
          ]
            .filter(Boolean)
            .join(' ')}
          {...props}
        />
        {error && <p className="text-xs text-[#E08585]">{error}</p>}
      </div>
    )
  }
)

Input.displayName = 'Input'
