import {
  LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip,
  ReferenceLine, ReferenceArea, ResponsiveContainer,
} from 'recharts'
import type { SessionSummary } from '../data/types'

interface ROMChartProps {
  data: SessionSummary[]
  targetLow: number
  targetHigh: number
}

export function ROMChart({ data, targetLow, targetHigh }: ROMChartProps) {
  return (
    <div className="bg-[#2A3337] border border-[#3D484E] p-4">
      <p className="text-[#A3B0B6] text-[9px] uppercase tracking-[0.18em] mb-4">
        Range of Motion — per session
      </p>
      <ResponsiveContainer width="100%" height={160}>
        <LineChart data={data} margin={{ top: 4, right: 8, bottom: 0, left: -20 }}>
          <CartesianGrid stroke="#3D484E" strokeDasharray="3 3" vertical={false} />
          <XAxis
            dataKey="session"
            tick={{ fill: '#A3B0B6', fontSize: 10 }}
            axisLine={false}
            tickLine={false}
            tickFormatter={v => `S${v}`}
          />
          <YAxis
            domain={[40, 100]}
            tick={{ fill: '#A3B0B6', fontSize: 10 }}
            axisLine={false}
            tickLine={false}
            tickFormatter={v => `${v}°`}
          />
          <Tooltip
            contentStyle={{ background: '#323C41', border: '1px solid #3D484E', borderRadius: 2, fontSize: 11 }}
            labelStyle={{ color: '#A3B0B6' }}
            itemStyle={{ color: '#6FB8C4' }}
            formatter={(v) => [`${v}°`, 'ROM']}
            labelFormatter={v => `Session ${v}`}
          />
          <ReferenceArea y1={targetLow} y2={targetHigh} fill="#6FB8C4" fillOpacity={0.08} />
          <ReferenceLine y={targetHigh} stroke="#6FB8C4" strokeDasharray="4 3" strokeOpacity={0.4} />
          <ReferenceLine y={targetLow}  stroke="#6FB8C4" strokeDasharray="4 3" strokeOpacity={0.4} />
          <Line
            type="monotone"
            dataKey="medianPeakDeg"
            stroke="#6FB8C4"
            strokeWidth={1.5}
            dot={{ r: 2.5, fill: '#6FB8C4', strokeWidth: 0 }}
            activeDot={{ r: 4, fill: '#86C5CF' }}
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  )
}

interface CompChartProps {
  data: SessionSummary[]
  limit: number
}

export function CompensationChart({ data, limit }: CompChartProps) {
  return (
    <div className="bg-[#2A3337] border border-[#3D484E] p-4">
      <p className="text-[#A3B0B6] text-[9px] uppercase tracking-[0.18em] mb-4">
        Trunk compensation — per session
      </p>
      <ResponsiveContainer width="100%" height={160}>
        <LineChart data={data} margin={{ top: 4, right: 8, bottom: 0, left: -20 }}>
          <CartesianGrid stroke="#3D484E" strokeDasharray="3 3" vertical={false} />
          <XAxis
            dataKey="session"
            tick={{ fill: '#A3B0B6', fontSize: 10 }}
            axisLine={false}
            tickLine={false}
            tickFormatter={v => `S${v}`}
          />
          <YAxis
            domain={[0, 16]}
            tick={{ fill: '#A3B0B6', fontSize: 10 }}
            axisLine={false}
            tickLine={false}
            tickFormatter={v => `${v}°`}
          />
          <Tooltip
            contentStyle={{ background: '#323C41', border: '1px solid #3D484E', borderRadius: 2, fontSize: 11 }}
            labelStyle={{ color: '#A3B0B6' }}
            itemStyle={{ color: '#E3A86B' }}
            formatter={(v) => [`${v}°`, 'Trunk']}
            labelFormatter={v => `Session ${v}`}
          />
          <ReferenceLine y={limit} stroke="#E08585" strokeDasharray="4 3" strokeOpacity={0.6} label={{ value: `limit ${limit}°`, fill: '#E08585', fontSize: 9, position: 'right' }} />
          <Line
            type="monotone"
            dataKey="trunkMeanDeg"
            stroke="#E3A86B"
            strokeWidth={1.5}
            dot={{ r: 2.5, fill: '#E3A86B', strokeWidth: 0 }}
            activeDot={{ r: 4, fill: '#E3A86B' }}
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  )
}
