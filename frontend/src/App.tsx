import { useCallback, useEffect, useMemo, useState } from "react";
import { differenceInMilliseconds, formatISO, parseISO, subDays } from "date-fns";
import aspireLogo from "/Aspire.png";
import "./App.css";

import { api } from "./lib/api";
import type { AnomalyPoint, ComparisonResult, ConsumptionSeries } from "./lib/types";
import { FilterBar, type FilterState } from "./components/FilterBar";
import { ConsumptionChart, type ChartType } from "./components/ConsumptionChart";
import { ComparisonTable } from "./components/ComparisonTable";
import { AnomalyList } from "./components/AnomalyList";
import { HeatmapChart } from "./components/HeatmapChart";
import ChatPage from "./pages/ChatPage";
import type { HeatmapSeries } from "./lib/types";

type Tab = "chart" | "comparison" | "anomalies" | "heatmap";
type Page = "dashboard" | "chat";

const CHART_TYPES: { value: ChartType; label: string }[] = [
  { value: "line", label: "Line" },
  { value: "bar", label: "Bar" },
  { value: "area", label: "Area" },
  { value: "stacked", label: "Stacked" },
];

const DEFAULT_RANGE_DAYS = 7;

function defaultFilters(): FilterState {
  const to = new Date();
  const from = subDays(to, DEFAULT_RANGE_DAYS);
  return {
    from: toLocalInput(from),
    to: toLocalInput(to),
    granularity: "Day",
    classCode: "",
    equipment: "",
  };
}

/** <input type="datetime-local"> wants "yyyy-MM-ddTHH:mm" in local time. */
function toLocalInput(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function toUtcIso(local: string): string {
  // Treat the local input as wall-clock and convert to UTC ISO.
  return new Date(local).toISOString();
}

export default function App() {
  const [page, setPage] = useState<Page>("dashboard");
  const [filters, setFilters] = useState<FilterState>(defaultFilters);
  const [tab, setTab] = useState<Tab>("chart");
  const [chartType, setChartType] = useState<ChartType>("line");
  const [showAnomaliesOnChart, setShowAnomaliesOnChart] = useState(true);

  const [series, setSeries] = useState<ConsumptionSeries[]>([]);
  const [comparison, setComparison] = useState<ComparisonResult[]>([]);
  const [anomalies, setAnomalies] = useState<AnomalyPoint[]>([]);
  const [heatmap, setHeatmap] = useState<HeatmapSeries[]>([]);

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const requestParams = useMemo(() => {
    const from = toUtcIso(filters.from);
    const to = toUtcIso(filters.to);
    const fromD = parseISO(from);
    const toD = parseISO(to);
    const lengthMs = differenceInMilliseconds(toD, fromD);
    const previousTo = from;
    const previousFrom = new Date(fromD.getTime() - lengthMs).toISOString();
    return {
      from,
      to,
      previousFrom,
      previousTo,
      granularity: filters.granularity,
      equipment: filters.equipment || undefined,
      classCode: filters.classCode || undefined,
    };
  }, [filters]);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [s, c, a, h] = await Promise.all([
        api.consumption(requestParams),
        api.compare(requestParams),
        api.anomalies(requestParams),
        api.heatmap({ from: requestParams.from, to: requestParams.to, equipment: requestParams.equipment, classCode: requestParams.classCode }),
      ]);
      setSeries(s);
      setComparison(c);
      setAnomalies(a);
      setHeatmap(h);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load data");
    } finally {
      setLoading(false);
    }
  }, [requestParams]);

  useEffect(() => {
    refresh();
  }, [refresh]);

  const highCount = anomalies.filter((a) => a.severity === "high").length;
  const totalConsumption = useMemo(
    () =>
      series.reduce(
        (acc, s) => acc + s.buckets.reduce((a, b) => a + (b.totalQuantity ?? 0), 0),
        0
      ),
    [series]
  );

  return (
    <div className="app-container">
      <header className="app-header">
        <a
          href="https://aspire.dev"
          target="_blank"
          rel="noopener noreferrer"
          aria-label="Visit Aspire website (opens in new tab)"
          className="logo-link"
        >
          <img src={aspireLogo} className="logo" alt="Aspire logo" />
        </a>
        <div>
          <h1 className="app-title">EnergyAI — Consumption Dashboard</h1>
          <p className="app-subtitle">Hourly / daily consumption, trends, and anomaly detection</p>
        </div>
        <nav className="app-nav">
          <button
            className={`nav-btn ${page === "dashboard" ? "active" : ""}`}
            onClick={() => setPage("dashboard")}
            type="button"
          >Dashboard</button>
          <button
            className={`nav-btn ${page === "chat" ? "active" : ""}`}
            onClick={() => setPage("chat")}
            type="button"
          >AI Chat</button>
        </nav>
      </header>

      {page === "chat" && <ChatPage />}

      {page === "dashboard" && <main className="main-content">

        {/* ── Left: chart area ── */}
        <div className="dashboard-main">
          {error && (
            <div className="error-message" role="alert">
              {error}
            </div>
          )}

          <section className="card" aria-live="polite">
          {/* Tab bar lives inside the card so it sits directly above the content */}
          <div className="tabs card-tabs" role="tablist">
            <TabBtn current={tab} value="chart" onClick={setTab}>
              Time series
            </TabBtn>
            <TabBtn current={tab} value="comparison" onClick={setTab}>
              Period comparison
            </TabBtn>
            <TabBtn current={tab} value="anomalies" onClick={setTab}>
              Anomalies ({anomalies.length})
            </TabBtn>
            <TabBtn current={tab} value="heatmap" onClick={setTab}>
              Heatmap
            </TabBtn>
          </div>

          {tab === "chart" && (
            <>
              <div className="chart-toolbar">
                <div className="chart-type-toggle" role="group" aria-label="Chart type">
                  {CHART_TYPES.map((t) => (
                    <button
                      key={t.value}
                      type="button"
                      className={`chart-type-btn ${chartType === t.value ? "active" : ""}`}
                      onClick={() => setChartType(t.value)}
                      aria-pressed={chartType === t.value}
                    >
                      {t.label}
                    </button>
                  ))}
                </div>
                <label className="anomaly-toggle">
                  <input
                    type="checkbox"
                    checked={showAnomaliesOnChart}
                    onChange={(e) => setShowAnomaliesOnChart(e.target.checked)}
                  />
                  <span>Mark anomalies ({anomalies.length})</span>
                </label>
              </div>
              <ConsumptionChart
                data={series}
                chartType={chartType}
                anomalies={showAnomaliesOnChart ? anomalies : []}
              />
            </>
          )}
          {tab === "comparison" && (
            <>
              <p className="muted small">
                Compared to the previous period of equal length (
                {formatISO(parseISO(requestParams.previousFrom)).slice(0, 10)} →{" "}
                {formatISO(parseISO(requestParams.previousTo)).slice(0, 10)}).
              </p>
              <ComparisonTable data={comparison} />
            </>
          )}
          {tab === "anomalies" && <AnomalyList data={anomalies} />}
          {tab === "heatmap"   && <HeatmapChart data={heatmap} />}
        </section>
        </div>{/* end .dashboard-main */}

        {/* ── Right: filters + KPIs ── */}
        <aside className="dashboard-sidebar">
          <FilterBar value={filters} onChange={setFilters} />

          <section className="kpi-row">
            <KpiCard label="Series" value={series.length.toString()} />
            <KpiCard
              label="Total consumption"
              value={totalConsumption.toLocaleString(undefined, { maximumFractionDigits: 2 })}
              hint={series[0]?.unitSymbol ?? ""}
            />
            <KpiCard
              label="Anomalies"
              value={anomalies.length.toString()}
              tone={highCount > 0 ? "alert" : undefined}
              hint={highCount > 0 ? `${highCount} high severity` : ""}
            />
            <div className="kpi-card actions">
              <button className="refresh-button" onClick={refresh} disabled={loading} type="button">
                {loading ? "Loading..." : "Refresh"}
              </button>
              <a
                className="refresh-button secondary"
                href={api.reportUrl(requestParams)}
                target="_blank"
                rel="noopener noreferrer"
              >
                Export .xlsx
              </a>
            </div>
          </section>
        </aside>

      </main>}

      <footer className="app-footer">
        <span className="muted small">EnergyAI · powered by .NET Aspire + React</span>
      </footer>
    </div>
  );
}

function KpiCard({
  label,
  value,
  hint,
  tone,
}: {
  label: string;
  value: string;
  hint?: string;
  tone?: "alert";
}) {
  return (
    <div className={`kpi-card ${tone ?? ""}`}>
      <div className="kpi-label">{label}</div>
      <div className="kpi-value">{value}</div>
      {hint && <div className="kpi-hint">{hint}</div>}
    </div>
  );
}

function TabBtn({
  current,
  value,
  onClick,
  children,
}: {
  current: Tab;
  value: Tab;
  onClick: (t: Tab) => void;
  children: React.ReactNode;
}) {
  return (
    <button
      role="tab"
      aria-selected={current === value}
      className={`tab-btn ${current === value ? "active" : ""}`}
      onClick={() => onClick(value)}
      type="button"
    >
      {children}
    </button>
  );
}

