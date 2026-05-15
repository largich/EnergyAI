import { useMemo, useState } from 'react'
import type { HeatmapSeries } from '../lib/types'

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------
const HOURS  = Array.from({ length: 24 }, (_, i) => i)
const DAYS   = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']
const DAYS_FULL = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
function hsl(value: number, min: number, max: number): string {
  if (max === min) return 'hsl(210 60% 92%)'
  const t   = (value - min) / (max - min) // 0..1
  const hue = Math.round(210 - t * 210)   // 210 (cool blue) → 0 (warm red)
  const sat = Math.round(50 + t * 40)
  const lit = Math.round(92 - t * 52)
  return `hsl(${hue} ${sat}% ${lit}%)`
}

function seriesLabel(s: HeatmapSeries): string {
  return `${s.equipmentCode} · ${s.className} · ${s.measurementName}`
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
interface Props {
  data: HeatmapSeries[]
}

export function HeatmapChart({ data }: Props) {
  const [selectedIdx, setSelectedIdx] = useState(0)
  const [tooltip, setTooltip]         = useState<{
    day: number; hour: number; value: number; samples: number; unit: string
  } | null>(null)

  const series = data[selectedIdx] ?? null

  const { min, max, cellMap } = useMemo(() => {
    if (!series) return { min: 0, max: 0, cellMap: new Map<string, number>() }

    const vals = series.cells.map(c => c.avgQuantity)
    const mn   = Math.min(...vals)
    const mx   = Math.max(...vals)
    const map  = new Map(series.cells.map(c => [`${c.dayOfWeek}-${c.hour}`, c.avgQuantity]))
    return { min: mn, max: mx, cellMap: map }
  }, [series])

  const sampleMap = useMemo(() => {
    if (!series) return new Map<string, number>()
    return new Map(series.cells.map(c => [`${c.dayOfWeek}-${c.hour}`, c.sampleCount]))
  }, [series])

  if (!data.length) {
    return <p className="muted small">No data for the selected range.</p>
  }

  return (
    <div className="heatmap-wrapper">
      {data.length > 1 && (
        <div className="heatmap-selector">
          <label htmlFor="hm-series">Series</label>
          <select
            id="hm-series"
            value={selectedIdx}
            onChange={e => setSelectedIdx(Number(e.target.value))}
          >
            {data.map((s, i) => (
              <option key={i} value={i}>{seriesLabel(s)}</option>
            ))}
          </select>
        </div>
      )}

      {series && (
        <>
          <p className="heatmap-subtitle muted small">
            Avg {series.unitSymbol} per hour slot · {seriesLabel(series)}
          </p>

          <div className="heatmap-grid">
            {/* Top-left corner spacer */}
            <div className="hm-corner" />

            {/* Hour labels */}
            {HOURS.map(h => (
              <div key={h} className="hm-hour-label">
                {h % 3 === 0 ? `${String(h).padStart(2, '0')}h` : ''}
              </div>
            ))}

            {/* Rows: one per day */}
            {DAYS.map((dayLabel, dow) => (
              <div key={`row-${dow}`} style={{ display: 'contents' }}>
                <div className="hm-day-label">{dayLabel}</div>
                {HOURS.map(h => {
                  const key   = `${dow}-${h}`
                  const value = cellMap.get(key)
                  const bg    = value !== undefined ? hsl(value, min, max) : 'hsl(0 0% 96%)'
                  const count = sampleMap.get(key) ?? 0
                  return (
                    <div
                      key={key}
                      className="hm-cell"
                      style={{ background: bg }}
                      onMouseEnter={() => value !== undefined && setTooltip({
                        day: dow, hour: h, value, samples: count, unit: series.unitSymbol
                      })}
                      onMouseLeave={() => setTooltip(null)}
                    />
                  )
                })}
              </div>
            ))}
          </div>

          {/* Legend */}
          <div className="heatmap-legend">
            <span className="muted small">{min.toFixed(2)}</span>
            <div className="hm-legend-bar" />
            <span className="muted small">{max.toFixed(2)} {series.unitSymbol}</span>
          </div>

          {/* Tooltip */}
          {tooltip && (
            <div className="hm-tooltip">
              <strong>{DAYS_FULL[tooltip.day]}</strong> {String(tooltip.hour).padStart(2,'0')}:00–{String(tooltip.hour+1).padStart(2,'0')}:00
              <br />
              Avg: <strong>{tooltip.value.toFixed(4)} {tooltip.unit}</strong>
              <br />
              <span className="muted small">{tooltip.samples} samples</span>
            </div>
          )}
        </>
      )}
    </div>
  )
}
