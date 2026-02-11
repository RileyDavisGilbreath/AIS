console.log('app.js loaded: version 2'); // simple cache-buster marker

const API_BASE = '';

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
  // Territories if present in your data
  '60': 'AS', '66': 'GU', '69': 'MP', '72': 'PR', '78': 'VI'
};

async function fetchJson(url) {
  const res = await fetch(API_BASE + url);
  if (!res.ok) throw new Error(`API ${res.status}: ${await res.text()}`);
  return res.json();
}

function updateStats(data) {
  try {
    const stateValueEl = document.querySelector('#state-avg .value');
    const blockGroupsValueEl = document.querySelector('#block-groups .value');

    if (stateValueEl) {
      stateValueEl.textContent =
        data?.avgWalkabilityScore?.toFixed(1) ?? '--';
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
            label: ctx => `${ctx.formattedValue} neighborhood areas`
          }
        }
      },
      scales: {
        x: {
          ticks: {
            autoSkip: true,
            maxTicksLimit: 8,
            maxRotation: 45,
            minRotation: 0
          },
          title: {
            display: true,
            text: 'Walkability score range'
          }
        },
        y: {
          beginAtZero: true,
          title: {
            display: true,
            text: 'Number of neighborhood areas'
          }
        }
      }
    }
  });
}

function initStatesChart(states) {
  const ctx = document.getElementById('states-chart').getContext('2d');
  const sorted = [...(states ?? [])]
    .filter(s => s.blockGroupCount > 0)
    .map(s => {
      const label = STATE_ABBREV_BY_FIPS[s.stateFips] || s.stateAbbrev || s.stateName || null;
      return label ? { ...s, __label: label } : null;
    })
    .filter(Boolean)
    .sort((a, b) => b.currentAvgWalkability - a.currentAvgWalkability)
    .slice(0, 15);
  if (sorted.length === 0) return;
  new Chart(ctx, {
    type: 'bar',
    data: {
      labels: sorted.map(s => s.__label),
      datasets: [{
        label: 'Avg Walkability',
        data: sorted.map(s => s.currentAvgWalkability),
        backgroundColor: 'rgba(63, 185, 80, 0.6)',
        borderColor: 'rgba(63, 185, 80, 1)',
        borderWidth: 1
      }]
    },
    options: {
      indexAxis: 'y',
      responsive: true,
      maintainAspectRatio: false,
      plugins: { legend: { display: false } },
      scales: {
        x: { beginAtZero: true },
        y: {
          ticks: {
            autoSkip: false,
            maxRotation: 0,
            minRotation: 0
          }
        }
      }
    }
  });
}

function initStateForecastChart(states) {
  const canvas = document.getElementById('state-forecast-chart');
  const ctx = canvas.getContext('2d');

  const top = [...(states ?? [])]
    .filter(s => s.blockGroupCount > 0)
    .map(s => {
      const label =
        STATE_ABBREV_BY_FIPS[s.stateFips] ||
        s.stateAbbrev ||
        s.stateName ||
        null;

      return label
        ? { ...s, __label: label }
        : null;
    })
    .filter(Boolean) // drop any state we couldn't label
    .sort((a, b) => b.predictedAvgWalkability - a.predictedAvgWalkability)
    .slice(0, 20); // limit to top 20 for performance

  if (top.length === 0) return;

  // Use only human-readable labels; we've already filtered out unknowns
  const labels = top.map(s => s.__label);
  const current = top.map(s => s.currentAvgWalkability);
  const predicted = top.map(s => s.predictedAvgWalkability);

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
      animation: false, // prevent chart from resizing/animated growth
      layout: {
        padding: {
          left: 10,
          right: 10,
          top: 10,
          bottom: 10
        }
      },
      scales: {
        x: { beginAtZero: true },
        y: {
          ticks: {
            autoSkip: false, // show all labels
            maxRotation: 0,
            minRotation: 0
          }
        }
      }
    }
  });
}

async function init() {
  try {
    const [stateStats, stateForecast] = await Promise.all([
      fetchJson('/api/stats/state'),
      fetchJson('/api/stats/state-forecast?years=10')
    ]);

    updateStats(stateStats);
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
