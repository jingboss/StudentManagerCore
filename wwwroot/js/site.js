// StudentManagerCore - Main JavaScript
console.log('华强学校信息管理中心已加载');

$(function () {
    // Auto-dismiss alerts after 5 seconds
    setTimeout(function () {
        $('.alert-dismissible').fadeOut('slow');
    }, 3000);
});

// ========== 全局 Toast 通知 ==========
function showToast(message, type) {
    type = type || 'success';
    var colors = {
        success: { bg: '#d1e7dd', border: '#badbcc', text: '#0f5132', icon: 'bi-check-circle-fill' },
        danger: { bg: '#f8d7da', border: '#f5c2c7', text: '#842029', icon: 'bi-exclamation-triangle-fill' },
        warning: { bg: '#fff3cd', border: '#ffecb5', text: '#664d03', icon: 'bi-exclamation-circle-fill' },
        info: { bg: '#cff4fc', border: '#b6effb', text: '#055160', icon: 'bi-info-circle-fill' }
    };
    var c = colors[type] || colors.info;
    var toast = $(
        '<div style="position:fixed;top:20px;right:20px;z-index:99999;background:' + c.bg +
        ';color:' + c.text + ';border:1px solid ' + c.border +
        ';border-radius:8px;padding:12px 20px;font-size:14px;box-shadow:0 4px 12px rgba(0,0,0,0.15);max-width:400px;display:flex;align-items:center;gap:8px;">' +
        '<i class="bi ' + c.icon + '"></i><span>' + message + '</span></div>'
    ).appendTo('body').fadeIn(200);
    setTimeout(function () { toast.fadeOut(400, function () { $(this).remove(); }); }, 4000);
}

// ========== 全局确认弹窗（替代 confirm()） ==========
function showConfirm(message, callback) {
    var modal = $('#globalConfirmModal');
    if (modal.length === 0) {
        // 如果页面上没有 Modal，fallback 到 confirm
        if (confirm(message)) callback();
        return;
    }
    $('#globalConfirmMessage').text(message);
    modal.data('callback', callback);
    modal.modal('show');
}

// 确认按钮点击
$(document).on('click', '#globalConfirmBtn', function () {
    var modal = $('#globalConfirmModal');
    var cb = modal.data('callback');
    modal.modal('hide');
    if (typeof cb === 'function') cb();
});

// ========== 全局 AJAX 加载指示器 ==========
$(document).on('ajaxStart', function () {
    var overlay = $('#globalLoadingOverlay');
    if (overlay.length === 0) {
        overlay = $(
            '<div id="globalLoadingOverlay" style="position:fixed;top:0;left:0;width:100%;height:100%;' +
            'background:rgba(255,255,255,0.6);z-index:99998;display:flex;align-items:center;justify-content:center;' +
            'flex-direction:column;">' +
            '<div class="spinner-border text-primary" style="width:3rem;height:3rem;" role="status"></div>' +
            '<div class="mt-2 text-muted small">加载中...</div></div>'
        ).appendTo('body');
    }
    overlay.fadeIn(100);
}).on('ajaxStop', function () {
    $('#globalLoadingOverlay').fadeOut(200);
});
