import {
  LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip,
  ReferenceLine, ReferenceArea, ResponsiveContainer, Legend,
} from 'recharts'
import type { SessionSummary } from '../data/types'

interface SessionTrendChartProps {
  data: SessionSummary[]
  targetLow: number
  targetHigh: number
  trunkLimit: number
}

export function SessionTrendChart({ data, targetLow, targetHigh, trunkLimit }: SessionTrendChartProps) {
  return (
    <ResponsiveContainer width="100%" height={180}>
      <LineChart data={data} margin={{ top: 4, right: 48, bottom: 0, left: -16 }}>
        <CartesianGrid stroke="#E8EDF2" strokeDasharray="3 3" vertical={false} />
        <XAxis
          dataKey="session"
          tick={{ fill: '#6B7280', fontSize: 11 }}
          axisLine={false}
          tickLine={false}
          tickFormatter={v => `S${v}`}
        />
        <YAxis
          domain={[0, 'auto']}
          tick={{ fill: '#6B7280', fontSize: 11 }}
          axisLine={false}
          tickLine={false}
          tickFormatter={v => `${v}°`}
        />
        <Tooltip
          contentStyle={{ background: '#fff', border: '1px solid #E2E8F0', borderRadius: 4, fontSize: 12 }}
          labelStyle={{ color: '#6B7280' }}
          labelFormatter={v => `Session ${v}`}
          formatter={(v: number, name: string) => [`${v}°`, name]}
        />
        <Legend
          wrapperStyle={{ fontSize: 11, color: '#6B7280', paddingTop: 8 }}
          iconType="line"
          iconSize={12}
        />
        <ReferenceArea y1={targetLow} y2={targetHigh} fill="#1666C0" fillOpacity={0.05} />
        <ReferenceLine y={targetHigh} stroke="#1666C0" strokeDasharray="4 3" strokeOpacity={0.35} />
        <ReferenceLine y={targetLow}  stroke="#1666C0" strokeDasharray="4 3" strokeOpacity={0.35} />
        <ReferenceLine
          y={trunkLimit}
          stroke="#C67C1A"
          strokeDasharray="4 3"
          strokeOpacity={0.6}
          label={{ value: `trunk limit ${trunkLimit}°`, fill: '#C67C1A', fontSize: 10, position: 'right' }}
        />
        <Line
          type="monotone"
          dataKey="medianPeakDeg"
          name="Peak ROM"
          stroke="#1666C0"
          strokeWidth={1.5}
          dot={{ r: 2, fill: '#1666C0', strokeWidth: 0 }}
          activeDot={{ r: 3.5, fill: '#1255A3' }}
        />
        <Line
          type="monotone"
          dataKey="trunkMeanDeg"
          name="Trunk dev."
          stroke="#C67C1A"
          strokeWidth={1.5}
          strokeDasharray="4 2"
          dot={{ r: 2, fill: '#C67C1A', strokeWidth: 0 }}
          activeDot={{ r: 3.5, fill: '#C67C1A' }}
        />
      </LineChart>
    </ResponsiveContainer>
  )
}
