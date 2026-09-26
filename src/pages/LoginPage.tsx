import { useState } from 'react'
import { useNavigate, useSearch } from '@tanstack/react-router'
import { Button } from '../ui/Button'
import { Input } from '../ui/Input'

export function LoginPage() {
  const navigate = useNavigate()
  const search = useSearch({ from: '/login' }) as { mode?: string }
  const [mode, setMode] = useState<'login' | 'signup'>(
    search.mode === 'signup' ? 'signup' : 'login'
  )
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    navigate({ to: '/portal' })
  }

  return (
    <div className="relative w-full flex-1 bg-[#232B2F] flex flex-col items-center justify-center overflow-hidden">
      <div className="relative z-10 w-full max-w-[360px] px-6">
        <Button variant="ghost" size="sm" className="mb-10 -ml-1" onClick={() => navigate({ to: '/' })}>
          ← Kinesthetic
        </Button>

        <p className="text-[#A3B0B6] text-[10px] tracking-[0.2em] uppercase mb-3">
          {mode === 'login' ? 'Clinician Access' : 'New Account'}
        </p>
        <h2 className="text-2xl font-bold text-[#E4E9EB] mb-7 tracking-tight">
          {mode === 'login' ? 'Log in to your portal' : 'Create your account'}
        </h2>

        <form onSubmit={handleSubmit} className="flex flex-col gap-3">
          <Input type="email" placeholder="you@hospital.com" value={email} onChange={e => setEmail(e.target.value)} />
          <Input type="password" placeholder="Password" value={password} onChange={e => setPassword(e.target.value)} />
          <Button type="submit" fullWidth size="lg" className="mt-2">
            {mode === 'login' ? 'Enter portal' : 'Create account'}
          </Button>
        </form>

        <p className="mt-6 text-center text-[#A3B0B6] text-sm">
          {mode === 'login' ? 'No account? ' : 'Have an account? '}
          <button
            type="button"
            onClick={() => setMode(mode === 'login' ? 'signup' : 'login')}
            className="text-[#6FB8C4] hover:text-[#86C5CF] transition-colors font-medium"
          >
            {mode === 'login' ? 'Sign up' : 'Log in'}
          </button>
        </p>
      </div>
    </div>
  )
}
