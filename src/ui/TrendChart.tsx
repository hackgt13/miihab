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
    <div className="bg-white border border-[#DDE2E8] rounded-md p-4 shadow-sm">
      <p className="text-[#5A6472] text-[9px] uppercase tracking-[0.18em] mb-4">
        Range of Motion — per session
      </p>
      <ResponsiveContainer width="100%" height={160}>
        <LineChart data={data} margin={{ top: 4, right: 8, bottom: 0, left: -20 }}>
          <CartesianGrid stroke="#DDE2E8" strokeDasharray="3 3" vertical={false} />
          <XAxis
            dataKey="session"
            tick={{ fill: '#5A6472', fontSize: 10 }}
            axisLine={false}
            tickLine={false}
            tickFormatter={v => `S${v}`}
          />
          <YAxis
            domain={[40, 100]}
            tick={{ fill: '#5A6472', fontSize: 10 }}
            axisLine={false}
            tickLine={false}
            tickFormatter={v => `${v}°`}
          />
          <Tooltip
            contentStyle={{ background: '#FFFFFF', border: '1px solid #DDE2E8', borderRadius: 6, fontSize: 11 }}
            labelStyle={{ color: '#5A6472' }}
            itemStyle={{ color: '#1666C0' }}
            formatter={(v) => [`${v}°`, 'ROM']}
            labelFormatter={v => `Session ${v}`}
          />
          <ReferenceArea y1={targetLow} y2={targetHigh} fill="#1666C0" fillOpacity={0.06} />
          <ReferenceLine y={targetHigh} stroke="#1666C0" strokeDasharray="4 3" strokeOpacity={0.4} />
          <ReferenceLine y={targetLow}  stroke="#1666C0" strokeDasharray="4 3" strokeOpacity={0.4} />
          <Line
            type="monotone"
            dataKey="medianPeakDeg"
            stroke="#1666C0"
            strokeWidth={1.5}
            dot={{ r: 2.5, fill: '#1666C0', strokeWidth: 0 }}
            activeDot={{ r: 4, fill: '#1255A3' }}
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
    <div className="bg-white border border-[#DDE2E8] rounded-md p-4 shadow-sm">
      <p className="text-[#5A6472] text-[9px] uppercase tracking-[0.18em] mb-4">
        Trunk compensation — per session
      </p>
      <ResponsiveContainer width="100%" height={160}>
        <LineChart data={data} margin={{ top: 4, right: 8, bottom: 0, left: -20 }}>
          <CartesianGrid stroke="#DDE2E8" strokeDasharray="3 3" vertical={false} />
          <XAxis
            dataKey="session"
            tick={{ fill: '#5A6472', fontSize: 10 }}
            axisLine={false}
            tickLine={false}
            tickFormatter={v => `S${v}`}
          />
          <YAxis
            domain={[0, 16]}
            tick={{ fill: '#5A6472', fontSize: 10 }}
            axisLine={false}
            tickLine={false}
            tickFormatter={v => `${v}°`}
          />
          <Tooltip
            contentStyle={{ background: '#FFFFFF', border: '1px solid #DDE2E8', borderRadius: 6, fontSize: 11 }}
            labelStyle={{ color: '#5A6472' }}
            itemStyle={{ color: '#C67C1A' }}
            formatter={(v) => [`${v}°`, 'Trunk']}
            labelFormatter={v => `Session ${v}`}
          />
          <ReferenceLine y={limit} stroke="#C62828" strokeDasharray="4 3" strokeOpacity={0.6} label={{ value: `limit ${limit}°`, fill: '#C62828', fontSize: 9, position: 'right' }} />
          <Line
            type="monotone"
            dataKey="trunkMeanDeg"
            stroke="#C67C1A"
            strokeWidth={1.5}
            dot={{ r: 2.5, fill: '#C67C1A', strokeWidth: 0 }}
            activeDot={{ r: 4, fill: '#C67C1A' }}
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  )
}
