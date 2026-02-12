console.log('app.js loaded: version 2'); // simple cache-buster marker

const API_BASE = '';

// Make chart text larger by default so labels fill the space better
if (window.Chart && Chart.defaults && Chart.defaults.font) {
  Chart.defaults.font.size = 14;
}

// FIPS -> state abbreviation (no numeric labels in charts)
const STATE_ABBREV_BY_FIPS = {
  '01': 'AL', '02': 'AK', '04': 'AZ', '05': 'AR', '06': 'CA',
  '08': 'CO', '09': 'CT', '10': 'DE', '11': 'DC', '12': 'FL',
  '13': 'GA', '15': 'HI', '16': 'ID', '17': 'IL', '18': 'IN',
  '19': 'IA', '20': 'KS', '21': 'KY', '22': 'LA', '23': 'ME',
  '24': 'MD', '25': 'MA', '26': 'MI', '27': 'MN', '28': 'MS',
  '29': 'MO', '30': 'MT', '31': 'NE', '32': 'NV', '33': 'NH',
  '34': 'NJ', '35': 'NM', '36': 'NY', '37': 'NC', '38': 'ND',
  '39': 'OH', '40': 'OK', '41': 'OR', '42': 'PA', '44': 'RI',
  '45': 'SC', '46': 'SD', '47': 'TN', '48': 'TX', '49': 'UT',
  '50': 'VT', '51': 'VA', '53': 'WA', '54': 'WV', '55': 'WI',
  '56': 'WY',
  '60': 'AS', '66': 'GU', '69': 'MP', '72': 'PR', '78': 'VI'
};
const STATE_NAME_BY_FIPS = {
  '01': 'Alabama', '02': 'Alaska', '04': 'Arizona', '05': 'Arkansas', '06': 'California',
  '08': 'Colorado', '09': 'Connecticut', '10': 'Delaware', '11': 'District of Columbia', '12': 'Florida',
  '13': 'Georgia', '15': 'Hawaii', '16': 'Idaho', '17': 'Illinois', '18': 'Indiana',
  '19': 'Iowa', '20': 'Kansas', '21': 'Kentucky', '22': 'Louisiana', '23': 'Maine',
  '24': 'Maryland', '25': 'Massachusetts', '26': 'Michigan', '27': 'Minnesota', '28': 'Mississippi',
  '29': 'Missouri', '30': 'Montana', '31': 'Nebraska', '32': 'Nevada', '33': 'New Hampshire',
  '34': 'New Jersey', '35': 'New Mexico', '36': 'New York', '37': 'North Carolina', '38': 'North Dakota',
  '39': 'Ohio', '40': 'Oklahoma', '41': 'Oregon', '42': 'Pennsylvania', '44': 'Rhode Island',
  '45': 'South Carolina', '46': 'South Dakota', '47': 'Tennessee', '48': 'Texas', '49': 'Utah',
  '50': 'Vermont', '51': 'Virginia', '53': 'Washington', '54': 'West Virginia', '55': 'Wisconsin',
  '56': 'Wyoming',
  '60': 'American Samoa', '66': 'Guam', '69': 'Northern Mariana Islands', '72': 'Puerto Rico', '78': 'U.S. Virgin Islands'
};
const FIFTY_STATE_FIPS = Object.keys(STATE_ABBREV_BY_FIPS).filter(
  f => !['60', '66', '69', '72', '78'].includes(f)
);
const EXCLUDED_FIPS = ['60', '66', '69', '72', '78', '07'];
let STATES_CHART_STATE = [];
let FORECAST_CHART_STATE = [];

function normalizeFips(f) {
  const s = String(f ?? '').trim();
  return s.length === 1 ? '0' + s : s;
}

function mergeStatesWithApiData(apiStates) {
  const byFips = (apiStates ?? []).reduce((acc, s) => {
    const f = normalizeFips(s.stateFips ?? s.StateFips);
    acc[f] = s;
    return acc;
  }, {});
  return FIFTY_STATE_FIPS.map(fips => {
    const s = byFips[fips];
    const label = STATE_ABBREV_BY_FIPS[fips];
    return s
      ? { ...s, __label: label }
      : { stateFips: fips, currentAvgWalkability: 0, predictedAvgWalkability: 0, blockGroupCount: 0, __label: label };
  });
}

async function fetchJson(url) {
  const res = await fetch(API_BASE + url);
  if (!res.ok) throw new Error(`API ${res.status}: ${await res.text()}`);
  return res.json();
}

function getWalkabilityRating(score) {
  if (score == null || Number.isNaN(score)) return null;
  if (score < 5) return 'Low (car-dependent)';
  if (score < 10) return 'Moderate';
  if (score < 15) return 'Good';
  return 'Very walkable';
}

function updateStats(data) {
  try {
    const stateValueEl = document.querySelector('#state-avg .value');
    const blockGroupsValueEl = document.querySelector('#block-groups .value');
    const ratingEl = document.querySelector('#state-avg-rating');

    if (stateValueEl) {
      const score = data?.avgWalkabilityScore;
      stateValueEl.textContent = score != null ? score.toFixed(1) : '--';
    }

    if (ratingEl) {
      const score = data?.avgWalkabilityScore;
      const rating = getWalkabilityRating(score);
      ratingEl.textContent = rating ? rating : '';
    }

    if (blockGroupsValueEl) {
      blockGroupsValueEl.textContent =
        data?.blockGroupCount?.toLocaleString() ?? '--';
    }
  } catch (e) {
    console.error('updateStats failed', e);
  }
}

function initDistributionChart(data) {
  const ctx = document.getElementById('distribution-chart').getContext('2d');
  // Handle both camelCase (from API) and PascalCase (if serialization is different)
  const rawBuckets = data?.scoreDistribution ?? data?.ScoreDistribution ?? [];
  // Normalize + sort buckets so they read left-to-right from lowest to highest score
  const buckets = [...rawBuckets].sort((a, b) => {
    const aLabel = (a.bucket ?? a.Bucket ?? '').toString();
    const bLabel = (b.bucket ?? b.Bucket ?? '').toString();

    // Try numeric compare first (for ranges like "0–5", use the first number)
    const aNum = parseFloat(aLabel);
    const bNum = parseFloat(bLabel);
    if (!Number.isNaN(aNum) && !Number.isNaN(bNum)) return aNum - bNum;

    return aLabel.localeCompare(bLabel);
  });
  console.log('Distribution buckets:', buckets);
  
  if (buckets.length === 0) {
    console.warn('No distribution data available');
    return;
  }
  
  // Handle both camelCase and PascalCase property names
  const labels = buckets.map(b => b.bucket ?? b.Bucket ?? '');
  const counts = buckets.map(b => b.count ?? b.Count ?? 0);
  
  new Chart(ctx, {
    type: 'bar',
    data: {
      labels,
      datasets: [{
        label: 'Neighborhood Areas',
        data: counts,
        backgroundColor: 'rgba(88, 166, 255, 0.6)',
        borderColor: 'rgba(88, 166, 255, 1)',
        borderWidth: 1
      }]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: false },
        tooltip: {
          callbacks: {
            label: ctx => {
              const mid = parseFloat(String(ctx.label).split(/[-–]/)[0]);
              const r = !Number.isNaN(mid) ? getWalkabilityRating(mid) : null;
              return r
                ? `${ctx.formattedValue} neighborhood areas (${r})`
                : `${ctx.formattedValue} neighborhood areas`;
            }
          }
        }
      },
      scales: {
        x: {
          ticks: {
            autoSkip: true,
            maxTicksLimit: 8,
            maxRotation: 45,
            minRotation: 0,
            font: { size: 14 }
          },
          title: {
            display: true,
            text: 'Walkability score range',
            font: { size: 14 }
          }
        },
        y: {
          beginAtZero: true,
          title: {
            display: true,
            text: 'Number of neighborhood areas',
            font: { size: 14 }
          }
        }
      }
    }
  });
}

function initStatesChart(states) {
  const el = document.getElementById('states-chart');
  if (!el) return;
  const ctx = el.getContext('2d');
  const merged = mergeStatesWithApiData(states);
  const sorted = merged
    .sort((a, b) => (b.currentAvgWalkability ?? b.CurrentAvgWalkability ?? 0) - (a.currentAvgWalkability ?? a.CurrentAvgWalkability ?? 0));
  if (sorted.length === 0) return;
  STATES_CHART_STATE = sorted;
  new Chart(ctx, {
    type: 'bar',
    data: {
      labels: sorted.map(s => s.__label),
      datasets: [{
        label: 'Avg Walkability',
        data: sorted.map(s => s.currentAvgWalkability ?? s.CurrentAvgWalkability ?? 0),
        backgroundColor: 'rgba(63, 185, 80, 0.6)',
        borderColor: 'rgba(63, 185, 80, 1)',
        borderWidth: 1
      }]
    },
    options: {
      indexAxis: 'y',
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: false },
        tooltip: {
          callbacks: {
            afterLabel: ctx => {
              const r = getWalkabilityRating(ctx.raw);
              return r ? `(${r})` : '';
            }
          }
        }
      },
      onClick: (evt, elements) => {
        if (!elements?.length) return;
        const idx = elements[0].index;
        const s = STATES_CHART_STATE[idx];
        if (s) showStateDetailFromStateForecast(s);
      },
      scales: {
        x: { beginAtZero: true },
        y: {
          ticks: {
            autoSkip: false,
            maxRotation: 0,
            minRotation: 0,
            font: { size: 12 }
          }
        }
      }
    }
  });
}

function initStateForecastChart(states) {
  const canvas = document.getElementById('state-forecast-chart');
  if (!canvas) return;
  const ctx = canvas.getContext('2d');

  const merged = mergeStatesWithApiData(states);
  const sorted = merged
    .sort((a, b) => (b.predictedAvgWalkability ?? b.PredictedAvgWalkability ?? 0) - (a.predictedAvgWalkability ?? a.PredictedAvgWalkability ?? 0));

  if (sorted.length === 0) return;

  FORECAST_CHART_STATE = sorted;

  const labels = sorted.map(s => s.__label);
  const current = sorted.map(s => s.currentAvgWalkability ?? 0);
  const predicted = sorted.map(s => s.predictedAvgWalkability ?? 0);

  new Chart(ctx, {
    type: 'bar',
    data: {
      labels,
      datasets: [
        {
          label: 'Current Avg',
          data: current,
          backgroundColor: 'rgba(88, 166, 255, 0.6)',
          borderColor: 'rgba(88, 166, 255, 1)',
          borderWidth: 1
        },
        {
          label: 'Predicted Avg',
          data: predicted,
          backgroundColor: 'rgba(255, 193, 7, 0.6)',
          borderColor: 'rgba(255, 193, 7, 1)',
          borderWidth: 1
        }
      ]
    },
    options: {
      indexAxis: 'y',
      responsive: true,
      maintainAspectRatio: false,
      animation: false,
      plugins: {
        tooltip: {
          callbacks: {
            afterLabel: ctx => {
              const r = getWalkabilityRating(ctx.raw);
              return r ? `(${r})` : '';
            }
          }
        }
      },
      layout: {
        padding: {
          left: 10,
          right: 10,
          top: 10,
          bottom: 10
        }
      },
      onClick: (evt, elements) => {
        if (!elements?.length) return;
        const idx = elements[0].index;
        const s = FORECAST_CHART_STATE[idx];
        if (s) showStateDetailFromStateForecast(s);
      },
      scales: {
        x: { beginAtZero: true },
        y: {
          ticks: {
            autoSkip: false,
            maxRotation: 0,
            minRotation: 0,
            font: { size: 12 }
          }
        }
      }
    }
  });
}

function getRecommendation(score) {
  if (score < 5) return { priority: 'High', recommendation: 'Prioritize transit expansion, mixed-use development, and pedestrian infrastructure.' };
  if (score < 10) return { priority: 'Moderate', recommendation: 'Focus on intersection density, transit proximity, and land-use diversity.' };
  if (score < 15) return { priority: 'Maintain', recommendation: 'Continue current policies; consider incremental improvements.' };
  return { priority: 'Exemplary', recommendation: 'Share best practices with other states.' };
}

function showStateDetailFromStateForecast(s) {
  const card = document.getElementById('state-detail-card');
  const emptyEl = document.getElementById('state-detail-empty');
  const bodyEl = document.getElementById('state-detail-body');
  if (!card || !bodyEl) return;

  const rawFips = s.stateFips ?? s.StateFips;
  const fips = normalizeFips(rawFips);
  const abbrev = STATE_ABBREV_BY_FIPS[fips] ?? fips ?? '?';
  const fullName = STATE_NAME_BY_FIPS[fips] ?? 'Unknown state';
  const current = s.currentAvgWalkability ?? s.CurrentAvgWalkability ?? 0;
  const predicted = s.predictedAvgWalkability ?? s.PredictedAvgWalkability ?? current;
  const delta = predicted - current;
  const ratingNow = getWalkabilityRating(current) ?? '';
  const ratingFuture = getWalkabilityRating(predicted) ?? '';
  const bgCount = s.blockGroupCount ?? s.BlockGroupCount ?? 0;

  const deltaStr = `${delta >= 0 ? '+' : ''}${delta.toFixed(1)}`;

  bodyEl.innerHTML = `
    <div class="state-detail-header">
      <span class="state-badge">${abbrev}: ${fullName}</span>
    </div>
    <div class="state-detail-metrics">
      <div><strong>Current:</strong> ${current.toFixed(1)} ${ratingNow ? `(${ratingNow})` : ''}</div>
      <div><strong>Forecast:</strong> ${predicted.toFixed(1)} ${ratingFuture ? `(${ratingFuture})` : ''}</div>
      <div><strong>Change:</strong> ${deltaStr}</div>
      <div><strong>Neighborhood areas:</strong> ${bgCount.toLocaleString()}</div>
    </div>
  `;

  // Ensure the card is visible once we have something to show
  if (card.style.display === 'none') {
    card.style.display = 'block';
  }
  if (emptyEl) emptyEl.style.display = 'none';
  bodyEl.style.display = 'block';
}

function recommendationsFromForecast(forecast) {
  if (!forecast?.length) return [];
  return forecast
    .filter(s => (s.blockGroupCount ?? s.BlockGroupCount) > 0)
    .map(s => {
      const score = s.currentAvgWalkability ?? s.CurrentAvgWalkability ?? 0;
      const rawFips = s.stateFips ?? s.StateFips;
      const fips = normalizeFips(rawFips);
      if (!fips || EXCLUDED_FIPS.includes(fips)) return null;
      const abbrev = STATE_ABBREV_BY_FIPS[fips];
      if (!abbrev) return null; // drop states/territories we can't name or don't want
      const { priority, recommendation } = getRecommendation(score);
      return { stateAbbrev: abbrev, stateFips: fips, priority, recommendation, currentScore: score };
    })
    .filter(Boolean)
    .sort((a, b) => (a.currentScore ?? 0) - (b.currentScore ?? 0))
    .slice(0, 15);
}

function renderRecommendations(recommendations, fallbackForecast) {
  const list = document.getElementById('recommendations-list');
  if (!list) return;
  let items = Array.isArray(recommendations) && recommendations.length ? recommendations : null;
  if (!items && fallbackForecast?.length) {
    const sorted = fallbackForecast
      .filter(s => (s.blockGroupCount ?? s.BlockGroupCount) > 0)
      .sort((a, b) => (a.currentAvgWalkability ?? a.CurrentAvgWalkability ?? 0) - (b.currentAvgWalkability ?? b.CurrentAvgWalkability ?? 0))
      .slice(0, 15);
    items = sorted
      .map(s => {
        const score = s.currentAvgWalkability ?? s.CurrentAvgWalkability ?? 0;
        const rawFips = s.stateFips ?? s.StateFips;
        const fips = normalizeFips(rawFips);
        if (!fips || EXCLUDED_FIPS.includes(fips)) return null;
        const abbrev = STATE_ABBREV_BY_FIPS[fips];
        if (!abbrev) return null;
        const { priority, recommendation } = getRecommendation(score);
        return { stateAbbrev: abbrev, priority, recommendation, currentScore: score };
      })
      .filter(Boolean);
  }
  // For API-provided recommendations, normalize and drop any state without a known abbrev
  if (items?.length) {
    items = items
      .map(r => {
        const rawFips = r.stateFips ?? r.StateFips;
        const fips = normalizeFips(rawFips);
        if (!fips || EXCLUDED_FIPS.includes(fips)) return null;
        const abbrev = STATE_ABBREV_BY_FIPS[fips];
        if (!abbrev) return null;
        return { ...r, stateAbbrev: abbrev, stateFips: fips };
      })
      .filter(Boolean);
  }
  if (!items?.length) {
    list.innerHTML = '<li class="muted">No recommendations available.</li>';
    return;
  }
  list.innerHTML = items.map(r => {
    const pClass = `priority-${(r.priority || '').toLowerCase()}`;
    const scoreStr = r.currentScore != null ? ` (${Number(r.currentScore).toFixed(1)})` : '';
    return `<li><span class="state-badge">${r.stateAbbrev}${scoreStr}</span>
      <span class="${pClass}">${r.priority || ''}</span>: ${r.recommendation || ''}</li>`;
  }).join('');
}

async function init() {
  try {
    const [stateStats, stateForecast, recommendations] = await Promise.all([
      fetchJson('/api/stats/state'),
      fetchJson('/api/stats/state-forecast?years=10'),
      fetchJson('/api/stats/state-recommendations').catch(() => [])
    ]);

    updateStats(stateStats);
    renderRecommendations(recommendations, stateForecast);
    // Use embedded distribution if present, otherwise fetch from dedicated endpoint
    const distribution = stateStats?.scoreDistribution ?? stateStats?.ScoreDistribution;
    if (distribution?.length) {
      initDistributionChart(stateStats);
    } else {
      const distributionData = await fetchJson('/api/stats/distribution').catch(() => []);
      initDistributionChart({ scoreDistribution: distributionData });
    }
    initStatesChart(stateForecast);
    initStateForecastChart(stateForecast);

    const noData = !stateStats?.blockGroupCount && (!stateForecast || stateForecast.length === 0);
    if (noData) {
      document
        .querySelector('.stats-cards')
        ?.insertAdjacentHTML('afterend',
        '<p class="error">No data in database. Run the import to load walkability data (see ImportController / data.gov).</p>');
    }
  } catch (err) {
    console.error('init() failed', err);

    // Also guard this DOM lookup so even the error UI can't crash
    document
      .querySelector('.stats-cards')
      ?.insertAdjacentHTML(
        'afterend',
        `<p class="error">Failed to load data: ${err?.message}. Check console for stack trace.</p>`
      );
  }
}

init();
