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
import { format, parseISO } from "date-fns";
import type { AnomalyPoint, ConsumptionSeries } from "../lib/types";

export type ChartType = "line" | "bar" | "area" | "stacked";

interface Props {
  data: ConsumptionSeries[];
  /** Optional anomalies; when supplied, they are overlaid as coloured dots. */
  anomalies?: AnomalyPoint[];
  chartType?: ChartType;
  height?: number;
}

interface Row {
  t: string; // x-axis key — bucketStartUtc ISO string
  [seriesKey: string]: number | string | null;
}

const COLORS = [
  "#2563eb", "#16a34a", "#db2777", "#f59e0b",
  "#7c3aed", "#ea580c", "#0891b2", "#65a30d",
];

const SEVERITY_COLOR: Record<AnomalyPoint["severity"], string> = {
  high: "#ef4444",
  medium: "#f59e0b",
  low: "#3b82f6",
};

export function ConsumptionChart({ data, anomalies = [], chartType = "line", height = 380 }: Props) {
  // All hooks must run in the same order on every render, so they live above
  // any conditional return.
  const { rows, keys, unit, keyToSeries } = useMemo(() => buildRows(data), [data]);
  // Anomaly dots: map each anomaly to the series it belongs to so we can place
  // it on the correct y-value on the correct line. Skip anomalies that don't
  // match any currently-plotted series (they may belong to a filtered-out one).
  const anomalyMarkers = useMemo(
    () => buildAnomalyMarkers(anomalies, keyToSeries, rows),
    [anomalies, keyToSeries, rows]
  );

  if (data.length === 0) {
    return <div className="empty-state">No data for this window.</div>;
  }

  // Only show brush if we have enough points to be useful.
  const showBrush = rows.length >= 8;

  // Shared chart children (axes, grid, tooltip, legend, brush, anomaly dots)
  const sharedChildren = (
    <>
      <CartesianGrid strokeDasharray="3 3" stroke="#e5e7eb" />
      <XAxis
        dataKey="t"
        tick={{ fontSize: 12 }}
        tickFormatter={(v: string) => format(parseISO(v), "MMM d HH:mm")}
        minTickGap={32}
      />
      <YAxis
        tick={{ fontSize: 12 }}
        label={{ value: unit, angle: -90, position: "insideLeft" }}
      />
      <Tooltip
        labelFormatter={(v: string) => format(parseISO(v), "yyyy-MM-dd HH:mm 'UTC'")}
        formatter={(val) => [
          typeof val === "number" ? val.toLocaleString() : val == null ? "—" : String(val),
          "",
        ]}
      />
      <Legend wrapperStyle={{ fontSize: 12 }} />
      {anomalyMarkers.map((m, i) => (
        <ReferenceDot
          key={`anom-${i}`}
          x={m.t}
          y={m.y}
          r={5}
          fill={SEVERITY_COLOR[m.severity]}
          stroke="#fff"
          strokeWidth={1.5}
          ifOverflow="extendDomain"
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          {...({ isFront: true } as any)}
        />
      ))}
      {showBrush && (
        <Brush
          dataKey="t"
          height={22}
          travellerWidth={8}
          stroke="#8b5ecf"
          tickFormatter={(v: string) => format(parseISO(v), "MMM d")}
        />
      )}
    </>
  );

  return (
    <div style={{ width: "100%", height }}>
      <ResponsiveContainer>
        {chartType === "bar" ? (
          <BarChart data={rows} margin={chartMargin}>
            {sharedChildren}
            {keys.map((k, i) => (
              <Bar key={k} dataKey={k} fill={COLORS[i % COLORS.length]} />
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
                  fillOpacity={0.25}
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
                  fillOpacity={0.55}
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

const chartMargin = { top: 10, right: 16, left: 0, bottom: 0 };

interface KeyToSeries {
  [key: string]: ConsumptionSeries;
}

function buildRows(data: ConsumptionSeries[]): {
  rows: Row[];
  keys: string[];
  unit: string;
  keyToSeries: KeyToSeries;
} {
  const allBuckets = new Set<string>();
  for (const s of data) for (const b of s.buckets) allBuckets.add(b.bucketStartUtc);
  const sortedBuckets = Array.from(allBuckets).sort();

  const keys = data.map(seriesKey);
  const keyToSeries: KeyToSeries = {};
  data.forEach((s) => {
    keyToSeries[seriesKey(s)] = s;
  });

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

interface AnomalyMarker {
  t: string;
  y: number;
  severity: AnomalyPoint["severity"];
}

/**
 * Match anomalies to the currently-plotted series and pin them to the actual
 * bucket value on that series. We skip anomalies that don't belong to any
 * visible series or whose bucket isn't present in the rowset — they can't be
 * placed meaningfully.
 */
function buildAnomalyMarkers(
  anomalies: AnomalyPoint[],
  keyToSeries: KeyToSeries,
  rows: Row[]
): AnomalyMarker[] {
  const bucketSet = new Set(rows.map((r) => r.t));
  const keyBySeriesIdentity = new Map<string, string>();
  for (const [key, s] of Object.entries(keyToSeries)) {
    keyBySeriesIdentity.set(
      `${s.equipmentCode}|${s.classCode}|${s.measurementCode}`,
      key
    );
  }

  const markers: AnomalyMarker[] = [];
  for (const a of anomalies) {
    if (!bucketSet.has(a.bucketStartUtc)) continue;
    const matchKey = keyBySeriesIdentity.get(
      `${a.equipmentCode}|${a.classCode}|${a.measurementCode}`
    );
    if (!matchKey) continue;
    markers.push({ t: a.bucketStartUtc, y: a.quantity, severity: a.severity });
  }
  return markers;
}
