// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
// Minimal interactivity for placeholder UI
document.addEventListener('DOMContentLoaded', function () {
    // Smooth scroll for anchor links — offset by navbar height to avoid overlap
    var navOffset = 80; // fixed nav height + breathing room
    document.querySelectorAll('a[href^="#"]').forEach(function (anchor) {
        anchor.addEventListener('click', function (e) {
            var href = this.getAttribute('href');
            // Skip bare "#" links (used for modals / placeholders)
            if (!href || href === '#') return;
            var target = document.querySelector(href);
            if (target) {
                e.preventDefault();
                var top = target.getBoundingClientRect().top + window.pageYOffset - navOffset;
                window.scrollTo({ top: top, behavior: 'smooth' });
                // Update URL hash without jumping
                history.pushState(null, '', href);
            }
        });
    });

    // Scroll-spy: highlight the active landing nav link based on scroll position
    var sectionIds = ['features', 'benefits', 'how-it-works'];
    var navLinks = {};
    sectionIds.forEach(function (id) {
        // Collect all nav anchors pointing to this section (landing nav + footer)
        document.querySelectorAll('a[href="#' + id + '"]').forEach(function (a) {
            if (a.closest('.landing-nav')) {
                navLinks[id] = navLinks[id] || [];
                navLinks[id].push(a);
            }
        });
    });

    function updateActiveNav() {
        var scrollPos = window.scrollY + navOffset + 40;
        var currentId = '';
        sectionIds.forEach(function (id) {
            var section = document.getElementById(id);
            if (section && section.offsetTop <= scrollPos) {
                currentId = id;
            }
        });
        // Clear all, then set active
        Object.keys(navLinks).forEach(function (id) {
            (navLinks[id] || []).forEach(function (a) {
                a.classList.remove('active-section');
            });
        });
        if (currentId && navLinks[currentId]) {
            navLinks[currentId].forEach(function (a) {
                a.classList.add('active-section');
            });
        }
    }

    window.addEventListener('scroll', updateActiveNav, { passive: true });
    updateActiveNav();

    // If a nav link opens the auth modal, switch to requested tab
    var authModal = document.getElementById('authModal');
    if (authModal) {
        authModal.addEventListener('show.bs.modal', function (event) {
            var trigger = event.relatedTarget;
            if (!trigger) return;
            var tab = trigger.getAttribute('data-auth-tab');
            if (!tab) return;
            var tabTriggerEl = document.querySelector('#' + tab + '-tab');
            if (tabTriggerEl) {
                var tabInstance = new bootstrap.Tab(tabTriggerEl);
                tabInstance.show();
            }
        });
    }

    // If the page was loaded with ?auth=login or ?auth=register (or #login/#register), open modal
    try {
        var urlParams = new URLSearchParams(window.location.search);
        var authParam = urlParams.get('auth');
        if (!authParam && window.location.hash) {
            authParam = window.location.hash.replace('#', '');
        }
        if (authParam && authModal && window.bootstrap) {
            var tabTriggerEl = document.querySelector('#' + authParam + '-tab');
            if (tabTriggerEl) {
                var tabInstance = new bootstrap.Tab(tabTriggerEl);
                // show modal first then switch tab
                var modal = new bootstrap.Modal(authModal);
                modal.show();
                tabInstance.show();
            }
        }
    } catch (e) { console.error(e); }

    // Reveal-on-scroll animations for landing page
    var revealElements = document.querySelectorAll('.reveal-on-scroll');
    if (revealElements.length) {
        if ('IntersectionObserver' in window) {
            var observer = new IntersectionObserver(function (entries, obs) {
                entries.forEach(function (entry) {
                    if (entry.isIntersecting) {
                        entry.target.classList.add('is-visible');
                        obs.unobserve(entry.target);
                    }
                });
            }, { threshold: 0.15 });

            revealElements.forEach(function (el) {
                observer.observe(el);
            });
        } else {
            // Fallback: show all immediately
            revealElements.forEach(function (el) {
                el.classList.add('is-visible');
            });
        }
    }
});
