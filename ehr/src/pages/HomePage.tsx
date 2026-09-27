import { useNavigate } from '@tanstack/react-router'
import { Button } from '../ui/Button'

export function HomePage() {
  const navigate = useNavigate()

  return (
    <div className="relative w-full flex-1 bg-[#232B2F] flex flex-col items-center justify-center overflow-hidden">
      <div className="relative z-10 flex flex-col items-center text-center px-6">
        <div className="mb-8 inline-flex items-center px-3 py-1 border border-[#3D484E] text-[#A3B0B6] text-[10px] tracking-[0.2em] uppercase">
          MiiHab · Physician Portal
        </div>

        <h1 className="text-[clamp(2.8rem,8vw,7rem)] font-bold text-[#E4E9EB] tracking-tight leading-[0.92] mb-5">
          Welcome,<br />Doctor.
        </h1>

        <p className="text-[#A3B0B6] text-base max-w-sm mb-10 leading-relaxed">
          Every session your patients complete: measured, stored, ready when you are.
        </p>

        <div className="flex gap-2.5">
          <Button size="lg" onClick={() => navigate({ to: '/login' })}>
            Log in
          </Button>
          <Button size="lg" variant="outline" onClick={() => navigate({ to: '/login', search: { mode: 'signup' } })}>
            Sign up
          </Button>
        </div>
      </div>
    </div>
  )
}
