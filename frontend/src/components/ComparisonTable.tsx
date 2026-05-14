import type { ComparisonResult } from "../lib/types";

interface Props {
  data: ComparisonResult[];
}

export function ComparisonTable({ data }: Props) {
  if (data.length === 0) {
    return <div className="empty-state">No comparison data.</div>;
  }

  return (
    <div className="table-wrap">
      <table className="data-table">
        <thead>
          <tr>
            <th>Equipment</th>
            <th>Medium</th>
            <th>Measurement</th>
            <th className="num">Current</th>
            <th className="num">Previous</th>
            <th className="num">Δ</th>
            <th className="num">Δ %</th>
          </tr>
        </thead>
        <tbody>
          {data.map((r, i) => {
            const pctClass =
              r.deltaPercent === null
                ? ""
                : r.deltaPercent > 5
                ? "up"
                : r.deltaPercent < -5
                ? "down"
                : "";
            return (
              <tr key={i}>
                <td>{r.current.equipmentCode}</td>
                <td>{r.current.className}</td>
                <td>
                  {r.current.measurementName}{" "}
                  <span className="muted">({r.current.unitSymbol})</span>
                </td>
                <td className="num">{r.currentTotal.toLocaleString()}</td>
                <td className="num">{r.previousTotal.toLocaleString()}</td>
                <td className="num">{r.deltaAbsolute.toLocaleString()}</td>
                <td className={`num ${pctClass}`}>
                  {r.deltaPercent === null ? "—" : `${r.deltaPercent.toFixed(1)}%`}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
