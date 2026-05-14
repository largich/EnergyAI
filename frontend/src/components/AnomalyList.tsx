import { format, parseISO } from "date-fns";
import type { AnomalyPoint } from "../lib/types";

interface Props {
  data: AnomalyPoint[];
}

export function AnomalyList({ data }: Props) {
  if (data.length === 0) {
    return <div className="empty-state">No anomalies flagged for this window. 🎉</div>;
  }

  return (
    <div className="table-wrap">
      <table className="data-table">
        <thead>
          <tr>
            <th>Severity</th>
            <th>When (UTC)</th>
            <th>Equipment</th>
            <th>Medium</th>
            <th className="num">Observed</th>
            <th className="num">Expected</th>
            <th className="num">Δ %</th>
            <th className="num">Z</th>
            <th>Reason</th>
          </tr>
        </thead>
        <tbody>
          {data.map((a, i) => (
            <tr key={i} className={`severity-${a.severity}`}>
              <td>
                <span className={`pill pill-${a.severity}`}>{a.severity}</span>
              </td>
              <td>{format(parseISO(a.bucketStartUtc), "yyyy-MM-dd HH:mm")}</td>
              <td>{a.equipmentCode}</td>
              <td>
                {a.classCode} <span className="muted">({a.unitSymbol})</span>
              </td>
              <td className="num">{a.quantity.toLocaleString()}</td>
              <td className="num">{a.expectedQuantity.toLocaleString()}</td>
              <td className={`num ${a.deviationPercent > 0 ? "up" : "down"}`}>
                {a.deviationPercent.toFixed(1)}%
              </td>
              <td className="num">{a.zScore.toFixed(2)}</td>
              <td className="muted">{a.reason}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
