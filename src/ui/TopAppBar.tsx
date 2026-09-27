export function TopAppBar() {
  const counters = [
    { label: 'Needs Review',    count: 1, color: '#ef4444' },
    { label: 'Missed Sessions', count: 2, color: '#f59e0b' },
    { label: 'RTM Due',         count: 1, color: '#60a5fa' },
    { label: 'Messages',        count: 3, color: '#9BB4CC' },
  ]

  return (
    <div className="w-full shrink-0 flex items-center px-3 gap-3 bg-[#1a2332]" style={{ height: 36 }}>
      {/* Wordmark */}
      <div className="flex items-center gap-2 shrink-0">
        <span className="text-white font-bold text-[13px] tracking-tight">RehabMii</span>
        <span className="text-[#4B6A88] text-[10px] tracking-[0.15em] uppercase select-none">Physician Portal</span>
      </div>

      <div className="w-px h-4 bg-white/15 shrink-0" />

      {/* Counter pills */}
      <div className="flex items-center gap-1">
        {counters.map(c => (
          <button
            key={c.label}
            className="flex items-center gap-1.5 px-2 py-[3px] rounded-[2px] cursor-pointer hover:bg-white/10 transition-colors"
          >
            <span className="text-[10px] text-[#8BACC4]">{c.label}</span>
            <span className="text-[10px] font-bold tabular-nums min-w-[14px] text-center" style={{ color: c.color }}>
              {c.count}
            </span>
          </button>
        ))}
      </div>

      <div className="flex-1" />

      {/* Dr. Chen */}
      <button className="flex items-center gap-1.5 px-2 py-1 rounded-[2px] cursor-pointer hover:bg-white/10 transition-colors">
        <div className="w-5 h-5 rounded-full bg-[#2E5A8A] flex items-center justify-center shrink-0">
          <span className="text-white text-[8px] font-bold select-none">DC</span>
        </div>
        <span className="text-[#BFD4E8] text-[11px]">Dr. Chen</span>
        <span className="text-[#4B6A88] text-[10px] leading-none">▾</span>
      </button>
    </div>
  )
}
