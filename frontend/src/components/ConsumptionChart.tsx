import { useMemo } from "react";
import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  Brush,
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  ReferenceDot,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { format, parseISO, isValid } from "date-fns";
import type { AnomalyPoint, ConsumptionSeries, Granularity } from "../lib/types";

export type ChartType = "line" | "bar" | "area" | "stacked";

interface Props {
  data: ConsumptionSeries[];
  anomalies?: AnomalyPoint[];
  chartType?: ChartType;
  height?: number;
}

interface Row {
  t: string;
  [seriesKey: string]: number | string | null;
}

const COLORS = [
  "#7c92f5", "#34d399", "#f472b6", "#fbbf24",
  "#a78bfa", "#fb923c", "#22d3ee", "#a3e635",
];

const SEVERITY_COLOR: Record<AnomalyPoint["severity"], string> = {
  high:   "#ef4444",
  medium: "#f59e0b",
  low:    "#60a5fa",
};

// ---------------------------------------------------------------------------
// Tick formatter — safe against non-date values Recharts sometimes passes
// ---------------------------------------------------------------------------
function makeTickFormatter(granularity: Granularity) {
  return (v: unknown): string => {
    if (typeof v !== "string") return "";
    try {
      const d = parseISO(v);
      if (!isValid(d)) return "";
      switch (granularity) {
        case "Hour":  return format(d, "MMM d HH:mm");
        case "Day":   return format(d, "MMM d");
        case "Week":  return format(d, "MMM d");
        case "Month": return format(d, "MMM yy");
        default:      return format(d, "MMM d");
      }
    } catch {
      return "";
    }
  };
}

function tooltipLabelFormatter(v: unknown): string {
  if (typeof v !== "string") return String(v);
  try {
    const d = parseISO(v);
    return isValid(d) ? format(d, "yyyy-MM-dd HH:mm 'UTC'") : String(v);
  } catch {
    return String(v);
  }
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
export function ConsumptionChart({ data, anomalies = [], chartType = "line", height = 400 }: Props) {
  const { rows, keys, unit, keyToSeries } = useMemo(() => buildRows(data), [data]);
  const anomalyMarkers = useMemo(
    () => buildAnomalyMarkers(anomalies, keyToSeries, rows),
    [anomalies, keyToSeries, rows]
  );

  if (data.length === 0) {
    return <div className="empty-state">No data for this window.</div>;
  }

  const granularity: Granularity = data[0]?.granularity ?? "Day";
  const tickFormatter  = makeTickFormatter(granularity);
  const showBrush      = rows.length >= 8;

  // Recharts 2.x does NOT recurse into React Fragments — each child must be a
  // direct element in the chart's children array. Using an array with explicit
  // keys satisfies both React (key uniqueness) and Recharts (direct iteration).
  const sharedChildren = [
    <CartesianGrid key="grid" strokeDasharray="3 3" stroke="rgba(255,255,255,0.08)" />,
    <XAxis
      key="xaxis"
      dataKey="t"
      tick={{ fontSize: 11, fill: "#94a3b8" }}
      tickFormatter={tickFormatter}
      minTickGap={48}
      interval="preserveStartEnd"
    />,
    <YAxis
      key="yaxis"
      tick={{ fontSize: 11, fill: "#94a3b8" }}
      width={56}
      label={{ value: unit, angle: -90, position: "insideLeft", fill: "#94a3b8", fontSize: 11 }}
    />,
    <Tooltip
      key="tooltip"
      contentStyle={{
        background: "rgba(20,20,36,0.97)",
        border: "1px solid rgba(255,255,255,0.12)",
        borderRadius: 8,
        fontSize: 13,
        color: "#e2e8f0",
      }}
      labelStyle={{ color: "#94a3b8", marginBottom: 4 }}
      labelFormatter={tooltipLabelFormatter}
      formatter={(val, name) => [
        typeof val === "number" ? val.toLocaleString(undefined, { maximumFractionDigits: 4 }) : val == null ? "—" : String(val),
        name as string,
      ]}
    />,
    <Legend
      key="legend"
      verticalAlign="top"
      wrapperStyle={{ paddingBottom: 12 }}
      formatter={(value) => (
        <span style={{ color: "#cbd5e1", fontSize: "0.78rem" }}>{value}</span>
      )}
    />,
    // Anomaly markers rendered on top
    ...anomalyMarkers.map((m, i) => (
      <ReferenceDot
        key={`anom-${i}`}
        x={m.t}
        y={m.y}
        r={6}
        fill={SEVERITY_COLOR[m.severity]}
        stroke="#0f172a"
        strokeWidth={1.5}
        ifOverflow="extendDomain"
        label={{ value: "!", fill: SEVERITY_COLOR[m.severity], fontSize: 10, fontWeight: "bold" }}
      />
    )),
    showBrush ? (
      <Brush
        key="brush"
        dataKey="t"
        height={22}
        travellerWidth={8}
        stroke="#8b5ecf"
        fill="rgba(20,20,36,0.8)"
        tickFormatter={tickFormatter}
      />
    ) : null,
  ];

  return (
    <div style={{ width: "100%", height }}>
      <ResponsiveContainer>
        {chartType === "bar" ? (
          <BarChart data={rows} margin={chartMargin}>
            {sharedChildren}
            {keys.map((k, i) => (
              <Bar key={k} dataKey={k} fill={COLORS[i % COLORS.length]} opacity={0.85} />
            ))}
          </BarChart>
        ) : chartType === "area" ? (
          <AreaChart data={rows} margin={chartMargin}>
            {sharedChildren}
            {keys.map((k, i) => {
              const color = COLORS[i % COLORS.length];
              return (
                <Area
                  key={k}
                  type="monotone"
                  dataKey={k}
                  stroke={color}
                  fill={color}
                  fillOpacity={0.2}
                  strokeWidth={2}
                  connectNulls
                />
              );
            })}
          </AreaChart>
        ) : chartType === "stacked" ? (
          <AreaChart data={rows} margin={chartMargin}>
            {sharedChildren}
            {keys.map((k, i) => {
              const color = COLORS[i % COLORS.length];
              return (
                <Area
                  key={k}
                  type="monotone"
                  dataKey={k}
                  stackId="1"
                  stroke={color}
                  fill={color}
                  fillOpacity={0.5}
                  strokeWidth={1.5}
                />
              );
            })}
          </AreaChart>
        ) : (
          <LineChart data={rows} margin={chartMargin}>
            {sharedChildren}
            {keys.map((k, i) => (
              <Line
                key={k}
                type="monotone"
                dataKey={k}
                stroke={COLORS[i % COLORS.length]}
                dot={false}
                strokeWidth={2}
                connectNulls
              />
            ))}
          </LineChart>
        )}
      </ResponsiveContainer>
    </div>
  );
}

const chartMargin = { top: 8, right: 16, left: 0, bottom: 0 };

// ---------------------------------------------------------------------------
// Data helpers
// ---------------------------------------------------------------------------
interface KeyToSeries { [key: string]: ConsumptionSeries }

function buildRows(data: ConsumptionSeries[]) {
  const allBuckets = new Set<string>();
  for (const s of data) for (const b of s.buckets) allBuckets.add(b.bucketStartUtc);
  const sortedBuckets = Array.from(allBuckets).sort();

  const keys = data.map(seriesKey);
  const keyToSeries: KeyToSeries = {};
  data.forEach((s) => { keyToSeries[seriesKey(s)] = s; });

  const unit = data[0]?.unitSymbol ?? "";

  const rows: Row[] = sortedBuckets.map((t) => {
    const row: Row = { t };
    for (const s of data) {
      const match = s.buckets.find((b) => b.bucketStartUtc === t);
      row[seriesKey(s)] = match ? match.totalQuantity : null;
    }
    return row;
  });

  return { rows, keys, unit, keyToSeries };
}

function seriesKey(s: ConsumptionSeries): string {
  return `${s.equipmentCode} · ${s.className} (${s.unitSymbol})`;
}

interface AnomalyMarker { t: string; y: number; severity: AnomalyPoint["severity"] }

function buildAnomalyMarkers(
  anomalies: AnomalyPoint[],
  keyToSeries: KeyToSeries,
  rows: Row[]
): AnomalyMarker[] {
  if (!anomalies.length || !rows.length) return [];

  // Normalise timestamps to ms-since-epoch to avoid ISO string format differences
  // (e.g. with/without trailing 'Z', milliseconds, etc.)
  const rowByTime = new Map<number, string>();
  for (const r of rows) {
    try { rowByTime.set(new Date(r.t).getTime(), r.t); } catch { /* skip */ }
  }

  // Map series identity → display key
  const keyByIdentity = new Map<string, string>();
  for (const [key, s] of Object.entries(keyToSeries)) {
    keyByIdentity.set(`${s.equipmentCode}|${s.classCode}|${s.measurementCode}`, key);
  }

  const markers: AnomalyMarker[] = [];
  for (const a of anomalies) {
    let rowT: string | undefined;
    try { rowT = rowByTime.get(new Date(a.bucketStartUtc).getTime()); } catch { continue; }
    if (!rowT) continue;

    const matchKey = keyByIdentity.get(`${a.equipmentCode}|${a.classCode}|${a.measurementCode}`);
    if (!matchKey) continue;

    markers.push({ t: rowT, y: a.quantity, severity: a.severity });
  }
  return markers;
}
