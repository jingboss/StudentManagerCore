// 图表实例缓存
var charts = {};

// 销毁所有图表实例
function destroyAllCharts() {
    Object.keys(charts).forEach(function (id) {
        if (charts[id]) {
            charts[id].destroy();
            delete charts[id];
        }
    });
}

// 销毁已有图表实例
function destroyChart(id) {
    if (charts[id]) {
        charts[id].destroy();
        delete charts[id];
    }
}

// 渲染分数段分布柱状图（按subject汇总）
function renderSegmentBarChart(subjectStats) {
    destroyChart('chartSegment');

    var ctx = document.getElementById('chartSegment');
    if (!ctx) return;

    // 聚合所有科目的分数段数据
    var segLabels = ['0-59分', '60-69分', '70-79分', '80-89分', '90-100分'];
    var segKeys = ['seg0_59', 'seg60_69', 'seg70_79', 'seg80_89', 'seg90_100'];
    var segTotals = [0, 0, 0, 0, 0];

    for (var i = 0; i < subjectStats.length; i++) {
        var s = subjectStats[i];
        for (var k = 0; k < segKeys.length; k++) {
            segTotals[k] += parseInt(s[segKeys[k]]) || 0;
        }
    }

    var barColors = ['#dc3545', '#fd7e14', '#ffc107', '#198754', '#0d6efd'];

    charts['chartSegment'] = new Chart(ctx.getContext('2d'), {
        type: 'bar',
        data: {
            labels: segLabels,
            datasets: [{
                label: '人数',
                data: segTotals,
                backgroundColor: barColors,
                borderColor: barColors,
                borderWidth: 1
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: { duration: 400 },
            plugins: {
                legend: { display: false },
                tooltip: {
                    callbacks: {
                        label: function(context) {
                            return context.parsed.y + ' 人';
                        }
                    }
                }
            },
            scales: {
                y: {
                    beginAtZero: true,
                    ticks: {
                        stepSize: 1,
                        precision: 0
                    },
                    title: {
                        display: true,
                        text: '人数'
                    }
                },
                x: {
                    title: {
                        display: true,
                        text: '分数段'
                    }
                }
            }
        }
    });
}

// 渲染各科平均分对比图
function renderAvgComparisonChart(subjectStats) {
    destroyChart('chartAvgCompare');

    var ctx = document.getElementById('chartAvgCompare');
    if (!ctx) return;

    var subNames = [];
    var avgData = [];
    var fullData = [];

    for (var i = 0; i < subjectStats.length; i++) {
        var s = subjectStats[i];
        subNames.push(s.subjectName || ('科目' + (i + 1)));
        avgData.push(parseFloat(s.avgScore) || 0);
        fullData.push(parseFloat(s.fullScore) || 100);
    }

    charts['chartAvgCompare'] = new Chart(ctx.getContext('2d'), {
        type: 'bar',
        data: {
            labels: subNames,
            datasets: [
                {
                    label: '平均分',
                    data: avgData,
                    backgroundColor: 'rgba(13, 110, 253, 0.6)',
                    borderColor: '#0d6efd',
                    borderWidth: 1,
                    order: 2
                },
                {
                    label: '满分',
                    data: fullData,
                    type: 'line',
                    borderColor: '#dc3545',
                    backgroundColor: 'transparent',
                    borderWidth: 2,
                    borderDash: [5, 5],
                    pointRadius: 4,
                    pointBackgroundColor: '#dc3545',
                    pointBorderColor: '#dc3545',
                    fill: false,
                    order: 1
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: { duration: 400 },
            plugins: {
                legend: {
                    display: true,
                    position: 'top'
                },
                tooltip: {
                    callbacks: {
                        label: function(context) {
                            return context.dataset.label + ': ' + context.parsed.y.toFixed(1);
                        }
                    }
                }
            },
            scales: {
                y: {
                    beginAtZero: true,
                    title: {
                        display: true,
                        text: '分数'
                    }
                },
                x: {
                    title: {
                        display: true,
                        text: '科目'
                    }
                }
            }
        }
    });
}

// 入口函数
function renderScoreCharts(subjectStats) {
    if (!subjectStats || subjectStats.length === 0) return;
    // 使用 requestAnimationFrame 避免阻塞主线程
    if (typeof requestAnimationFrame !== 'undefined') {
        requestAnimationFrame(function () {
            renderSegmentBarChart(subjectStats);
            renderAvgComparisonChart(subjectStats);
        });
    } else {
        renderSegmentBarChart(subjectStats);
        renderAvgComparisonChart(subjectStats);
    }
}