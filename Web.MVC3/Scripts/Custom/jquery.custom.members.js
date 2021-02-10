(function ($) {
    $.fn.members = function (method) {

        if (methods[method]) {
            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        } else if (typeof method === 'object' || !method) {
            return methods.init.apply(this, arguments);
        } else {
            $.error('Method ' + method + ' does not exist on the plugin');
        }

    };

    var methods = {
        init: function (options) {
            var opts = $.extend({}, $.fn.members.defaults, options);
            var $this = this;
            
            $('[data-role="Confirm"]').click(function (e) {
                loadAsync(e, $(this).attr('href'),
                null,
                function (result) {
                    if (result) {
                        $(e.target).remove();
                    }
                });
                return cancelEvent(e);
            });
            
            $('[data-role="Reject"]').click(function (e) {
                var t = $(this);
                loadAsync(e, t.attr('href'),
                null,
                function (result) {
                    if (result) {
                        t.parents('[data-role="deleteContainer"]').remove();
                    }
                });
                return cancelEvent(e);
            });

            $('#lnkShowInvitations').on('click', function(e) {
                $('#invitations').toggle();
            });

            $('input[data-url]').on('click', function (e) {
                loadAsync(e, $(this).attr('data-url'),
                    { isPublic: $(this).is(':checked') },
                function (result) {
                    if (!result) {
                        alert('Įvyko klaida');
                    }
                });
            });

            $('#formInviteUser').submit(function (e) {
                inviteUser(e, $(e.target));
                return cancelEvent(e);
            });

            $('a.lnkReInvite').click(function (e) {
                loadAsync(e, $(e.target).attr('href'), null, function (result) {
                    if (result) {
                        $(e.target).hide();
                    }
                });
                return cancelEvent(e);
            });

            $('#lnkEditMembers').click(function (e) {
                loadAsync(e, $(this).attr('href'),
                null,
                function (result) {
                    $('#divMemberContainer').html(result.Content);
                    $('#lnkEditMembers').hide();
                });

                cancelEvent(e);
            });

            $this.on('click', '#btnSubmit', function (e) {
                var form = $(e.target).parents('form');
                var url = form.attr('action');
                loadAsync(e, url,
                    form.serialize(),
                    function (result) {
                        $('#divMemberContainer').html(result.Content);
                        $('#lnkEditMembers').show();
                    });
                cancelEvent(e);
            });

            $this.on('click', '#lnkCancel', function (e) {
                var url = $(e.target).attr('href');
                loadAsync(e, url,
                    null,
                    function (result) {
                        $('#divMemberContainer').html(result.Content);
                        $('#lnkEditMembers').show();
                    });
                cancelEvent(e);
            });

            return $this;
        },
        invite: function () {
            inviteUser(null, $('#formInviteUser'));
        }
    };

    function inviteUser(e, form) {
        loadAsync(e, form.attr('action'), form.serialize(), function (result) {
            if (result) {
                $('#listUsersToInvite').append(result.Content);
                $('#InvitedUser').val('');
                $('#UserId').val('');
            }
        });
    }

    $.fn.members.defaults = {
    };

})(jQuery);